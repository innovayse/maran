//! Tests for the startup convergence of the OPcache drop-ins.
//!
//! The pass is a thin logging layer over `maran_ops::php::reconcile_opcache_drop_ins`;
//! what these pin is that it reaches every installed version, that one version's
//! refusal does not stop the next, and that a host with no PHP is left alone.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::BTreeMap;
use std::path::Path;
use std::sync::Mutex;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{AdapterBinary, DistroFamily};
use maran_ops::php::{PhpHost, PhpOpError};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome, ConfigHost, SafeWriteError};

use super::*;

/// A [`PhpHost`] with a fixed set of installed versions that keeps the files
/// written to it, and refuses the write for the versions it is told to.
///
/// Its own fake rather than `ops`'s, because that one is `pub(crate)` behind
/// `#[cfg(test)]` in another crate.
struct FakeDropInHost {
    /// Versions whose pool directory "exists".
    installed: Vec<String>,
    /// Versions whose `php-fpm -t` refuses, matched against the target path.
    refusing: Vec<String>,
    /// The files written, path to content.
    files: Mutex<BTreeMap<String, String>>,
}

impl FakeDropInHost {
    /// A host with `installed` present, refusing the write for `refusing`.
    fn new(installed: &[&str], refusing: &[&str]) -> Self {
        Self {
            installed: installed.iter().map(|item| (*item).to_owned()).collect(),
            refusing: refusing.iter().map(|item| (*item).to_owned()).collect(),
            files: Mutex::new(BTreeMap::new()),
        }
    }

    /// The paths written so far.
    fn written(&self) -> Vec<String> {
        self.files.lock().unwrap().keys().cloned().collect()
    }
}

impl ConfigHost for FakeDropInHost {
    /// Never reached: the pass runs nothing outside the write protocol.
    fn run(
        &self,
        _program: &AdapterBinary,
        _arguments: &[&str],
        _class: CallClass,
    ) -> Result<CommandOutcome, SafeWriteError> {
        unreachable!("the drop-in pass spawns nothing outside the write protocol")
    }
}

impl PhpHost for FakeDropInHost {
    /// A pool directory exists when its version is one of the installed ones.
    fn directory_exists(&self, path: &Path) -> bool {
        let shown = path.display().to_string();
        self.installed
            .iter()
            .any(|version| shown.contains(version.as_str()))
    }

    /// Answers what was written, so a second pass sees a current file.
    fn read_config(&self, path: &Path) -> Option<String> {
        self.files
            .lock()
            .unwrap()
            .get(&path.display().to_string())
            .cloned()
    }

    /// Never reached: the pass creates no directory.
    fn create_directory(&self, _path: &Path, _mode: u32) -> Result<(), PhpOpError> {
        unreachable!("the drop-in pass creates no directory")
    }

    /// Never reached: the pass creates no directory.
    fn create_directories_as_account(
        &self,
        _account: &AccountName,
        _directories: &[&Path],
        _mode: u32,
    ) -> Result<(), PhpOpError> {
        unreachable!("the drop-in pass creates no directory")
    }

    /// Keeps the file, or refuses it when the target is under a refusing version.
    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        let shown = target.display().to_string();
        if self
            .refusing
            .iter()
            .any(|version| shown.contains(version.as_str()))
        {
            return Err(PhpOpError::PoolValidation {
                stderr: "bad ini".to_owned(),
            });
        }
        self.files
            .lock()
            .unwrap()
            .insert(shown, contents.to_owned());
        Ok(())
    }

    /// Never reached: the pass removes nothing.
    fn remove_config(
        &self,
        _target: &Path,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("the drop-in pass removes no file")
    }

    /// Never reached: the pass reloads only through the write protocol.
    fn validate_and_reload(
        &self,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), PhpOpError> {
        unreachable!("the drop-in pass reloads only through a write")
    }
}

/// A host with no PHP installed is reported as having no drop-in to converge,
/// and nothing is written: the inverse control for the tests below.
#[test]
fn a_host_with_no_php_has_nothing_to_converge_and_writes_nothing() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = FakeDropInHost::new(&[], &[]);

    assert!(reconcile_opcache_drop_ins(&host, adapter).is_empty());
    assert!(host.written().is_empty());
}

/// Every installed version gets a drop-in in its own ini directory.
#[test]
fn every_installed_version_gets_its_drop_in() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = FakeDropInHost::new(&["8.1", "8.3"], &[]);

    let outcomes = reconcile_opcache_drop_ins(&host, adapter);

    assert_eq!(outcomes.len(), 2);
    assert!(outcomes.iter().all(|outcome| outcome.result.is_ok()));
    let written = host.written();
    assert_eq!(written.len(), 2, "{written:?}");
    assert!(
        written
            .iter()
            .all(|path| path.ends_with("/99-maran-opcache.ini"))
    );
}

/// One version's refusal is reported against that version and the other is
/// still written: the pass never stops at the first failure.
#[test]
fn one_versions_refusal_does_not_stop_the_next_version() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = FakeDropInHost::new(&["8.1", "8.3"], &["8.3"]);

    let outcomes = reconcile_opcache_drop_ins(&host, adapter);

    let refused = outcomes
        .iter()
        .find(|outcome| outcome.version == "8.3")
        .unwrap();
    assert!(matches!(
        refused.result,
        Err(PhpOpError::PoolValidation { .. })
    ));
    let converged = outcomes
        .iter()
        .find(|outcome| outcome.version == "8.1")
        .unwrap();
    assert!(converged.result.is_ok());
    assert_eq!(host.written().len(), 1);
}
