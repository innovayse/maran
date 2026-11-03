//! Tests for the startup validation of the php-fpm pool trees.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints"): a failed unwrap here is the test reporting, and
//! the workspace lints are denied rather than warned, so the exemption is
//! declared file by file as every other test file in this tree declares it.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{AdapterBinary, DistroFamily};
use maran_ops::php::{PhpHost, PhpOpError, PoolTreeDecision};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome, ConfigHost, SafeWriteError};

use super::*;

/// A [`PhpHost`] that reports a fixed set of installed versions, records the
/// argv it was asked to run, and answers a configured outcome — so a test can
/// pin what the startup pool pass ASKS as well as what it decides.
///
/// Its own fake rather than `ops`'s, because that one is `pub(crate)` behind
/// `#[cfg(test)]` in another crate and is not reachable from here. It answers
/// only the three methods this pass uses and refuses the rest loudly, which is
/// the assertion that the pass writes nothing: a pass that wrote a pool file
/// would reach `write_config` and abort the test.
struct FakePoolHost {
    /// The pool directories that "exist", i.e. the installed versions.
    installed: Vec<String>,
    /// `php-fpm -t`'s answer: exit status and the reason it gives.
    validation: (i32, String),
    /// The reload's answer: exit status and the reason it gives.
    reload: (i32, String),
    /// Every argv the pass asked for, in order.
    calls: Mutex<Vec<Vec<String>>>,
}

impl FakePoolHost {
    /// A host with `installed` present, whose validator and reload both accept.
    fn new(installed: &[&str]) -> Self {
        Self {
            installed: installed.iter().map(|item| (*item).to_owned()).collect(),
            validation: (0, String::new()),
            reload: (0, String::new()),
            calls: Mutex::new(Vec::new()),
        }
    }

    /// Every argv the pass asked for.
    fn calls(&self) -> Vec<Vec<String>> {
        self.calls.lock().unwrap().clone()
    }
}

impl ConfigHost for FakePoolHost {
    /// Records the argv; the outcome is decided by the caller below.
    fn run(
        &self,
        program: &AdapterBinary,
        arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_string()];
        command.extend(arguments.iter().map(|argument| (*argument).to_owned()));
        self.calls.lock().unwrap().push(command);

        Ok(CommandOutcome {
            status: 0,
            stdout: String::new(),
            stderr: String::new(),
        })
    }
}

impl PhpHost for FakePoolHost {
    /// A pool directory exists when its version is one of the installed ones,
    /// which is exactly how the real host answers "is this version installed?".
    fn directory_exists(&self, path: &Path) -> bool {
        let shown = path.display().to_string();
        self.installed
            .iter()
            .any(|version| shown.contains(version.as_str()))
    }

    /// Never reached: the startup pass reads no pool.
    fn read_config(&self, _path: &Path) -> Option<String> {
        unreachable!("the startup pool pass reads no pool file")
    }

    /// Never reached: the startup pass creates nothing.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        unreachable!("the startup pool pass creates no directory")
    }

    /// Never reached: the startup pass creates nothing.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        unreachable!("the startup pool pass creates no directory")
    }

    /// Never reached, and that is the assertion: the pass writes nothing.
    fn write_config(
        &self,
        _target: &Path,
        _contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("the startup pool pass writes no pool file")
    }

    /// Never reached, and that is the assertion: the pass removes nothing.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("the startup pool pass removes no pool file")
    }

    /// Records both commands and answers the configured outcomes.
    fn validate_and_reload(
        &self,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        ConfigHost::run(
            self,
            validator.program,
            validator.arguments,
            validator.class,
        )?;
        if self.validation.0 != 0 {
            return Err(PhpOpError::PoolValidation {
                stderr: self.validation.1.clone(),
            });
        }

        ConfigHost::run(self, reload.program, reload.arguments, CallClass::Reload)?;
        if self.reload.0 != 0 {
            return Err(PhpOpError::ReloadFailed {
                stderr: self.reload.1.clone(),
            });
        }

        Ok(())
    }
}

/// A host with no PHP installed is reported as having no pool tree, and the
/// pass spawns nothing at all — the inverse control for every assertion below,
/// because a pass that always ran `php-fpm -t` for all six supported versions
/// would run six processes at every start on a host with no PHP.
#[test]
fn a_host_with_no_php_has_no_pool_tree_and_spawns_nothing() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = FakePoolHost::new(&[]);

    assert!(reconcile_php_pool_trees(&host, adapter).is_empty());
    assert!(host.calls().is_empty());
}

/// Each installed version's pool tree is validated with that version's own
/// php-fpm binary and reloaded through that version's own service, both from
/// the distro adapter.
///
/// UNOBSERVED HERE: whether the adapter spells either of them correctly. The
/// expectations come from the same adapter the code reads them from, so this
/// pins the SEAM — the startup question is the one every pool write asks — not
/// the values. The `-t` and the `reload` are literals.
#[test]
fn each_installed_versions_tree_is_validated_and_reloaded_through_its_own_service() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = FakePoolHost::new(&["8.3"]);

    let version = PhpVersion::parse("8.3").unwrap();

    let outcomes = reconcile_php_pool_trees(&host, adapter);

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].version, "8.3");
    assert_eq!(outcomes[0].decision, PoolTreeDecision::Applied);
    assert_eq!(
        host.calls(),
        vec![
            vec![
                adapter.php_fpm_binary(&version).to_string(),
                "-t".to_owned()
            ],
            vec![
                adapter.service_manager().to_string(),
                "reload".to_owned(),
                "--".to_owned(),
                adapter.php_fpm_service("8.3"),
            ],
        ]
    );
}

/// A pool tree php-fpm rejects is reported as rejected, carries the validator's
/// own reason, is NOT reloaded, and does not stop the start: this process is the
/// operator's only remote repair tool.
#[test]
fn a_rejected_pool_tree_is_reported_not_reloaded_and_does_not_stop_the_start() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let mut host = FakePoolHost::new(&["8.3"]);
    host.validation = (1, "unexpected end of file".to_owned());

    let version = PhpVersion::parse("8.3").unwrap();

    let outcomes = reconcile_php_pool_trees(&host, adapter);

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].decision, PoolTreeDecision::Refused);
    assert_eq!(outcomes[0].stderr, "unexpected end of file");
    assert_eq!(
        host.calls(),
        vec![vec![
            adapter.php_fpm_binary(&version).to_string(),
            "-t".to_owned()
        ]],
        "a tree php-fpm refused must not then be reloaded onto the running service"
    );
}

/// A reload that refuses on a tree that validated is a different job for an
/// operator — usually a php-fpm that is not running — and is reported as its own
/// outcome rather than folded into a rejection.
#[test]
fn a_pool_reload_that_refuses_is_reported_apart_from_a_rejected_tree() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let mut host = FakePoolHost::new(&["8.3"]);
    host.reload = (1, "Unit php8.3-fpm.service not loaded.".to_owned());

    let outcomes = reconcile_php_pool_trees(&host, adapter);

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].decision, PoolTreeDecision::NotReloaded);
    assert!(outcomes[0].stderr.contains("not loaded"));
}
