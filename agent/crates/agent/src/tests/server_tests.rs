//! Tests for the startup reconciliation of the web configuration tree.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints"): a failed unwrap here is the test reporting, and
//! the workspace lints are denied rather than warned, so the exemption is
//! declared file by file as every other test file in this tree declares it.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::Mutex;

use maran_agent_core::validation::system::name::AccountName;

use maran_distro::DistroFamily;
use maran_ops::php::{PhpHost, PhpOpError, PoolTreeDecision};
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::safe_write::{CommandOutcome, ConfigHost, SafeWriteError};
use maran_ops::sites::{LogSink, LogTailRequest, SiteMaintenanceHost, SitesOpError, TailEnd};

use super::{WebTreeReconciliation, reconcile_php_pool_trees, reconcile_web_tree};

/// The source of the file these tests belong to, read so that
/// [`startup_reconcilers`] can name what `serve` actually calls rather than
/// what a reader remembers it calling.
const SERVER_SOURCE: &str = include_str!("../server.rs");

/// What the fake host below answers when the startup pass asks it.
enum Answer {
    /// The tree validates and the reload takes.
    Accept,
    /// The validator rejects what is on disk.
    RejectValidation,
    /// The tree validates but the reload refuses it.
    RefuseReload,
}

/// A [`SiteMaintenanceHost`] that records the argv it was asked to run and
/// answers a configured outcome, so a test can pin what the startup pass
/// ASKS as well as what it decides.
struct RecordingHost {
    answer: Answer,
    validator: Mutex<Option<(String, Vec<String>)>>,
    reload: Mutex<Option<(String, Vec<String>)>>,
}

impl RecordingHost {
    /// Creates a host that answers `answer` and has recorded nothing yet.
    fn new(answer: Answer) -> Self {
        Self {
            answer,
            validator: Mutex::new(None),
            reload: Mutex::new(None),
        }
    }
}

impl SiteMaintenanceHost for RecordingHost {
    /// Records both commands and answers the configured outcome.
    fn validate_and_reload(
        &self,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        *self.validator.lock().unwrap() = Some((
            validator.program.to_owned(),
            validator
                .arguments
                .iter()
                .map(|a| (*a).to_owned())
                .collect(),
        ));
        *self.reload.lock().unwrap() = Some((
            reload.program.to_owned(),
            reload.arguments.iter().map(|a| (*a).to_owned()).collect(),
        ));

        match self.answer {
            Answer::Accept => Ok(()),
            Answer::RejectValidation => Err(SitesOpError::NginxValidation {
                stderr: "duplicate server_name".to_owned(),
            }),
            Answer::RefuseReload => Err(SitesOpError::ReloadFailed {
                stderr: "unit is not loaded".to_owned(),
            }),
        }
    }

    /// Never reached: the startup pass tails no log.
    fn tail_log(
        &self,
        _request: &LogTailRequest,
        _sink: &mut dyn LogSink,
    ) -> Result<TailEnd, SitesOpError> {
        unreachable!("the startup web-tree pass never tails a log")
    }
}

/// The startup pass asks the web server's own validator, from the distro
/// adapter, with the validate-only flag — it does not invent a check of its
/// own.
///
/// UNOBSERVED HERE: whether `nginx_binary()` names the right binary. The
/// expectation is read from the same adapter the code reads it from, so this
/// pins the SEAM — that the startup question is the one every individual
/// config write asks — and not the value. The flag is a literal, so a pass
/// that stopped asking a validate-only question would be caught.
#[test]
fn the_startup_pass_validates_with_the_web_servers_own_validate_only_flag() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = RecordingHost::new(Answer::Accept);

    reconcile_web_tree(&host, adapter);

    let (program, arguments) = host.validator.lock().unwrap().clone().unwrap();
    assert_eq!(program, adapter.nginx_binary());
    assert!(
        program.starts_with('/'),
        "a root process must not spawn a program by a bare name"
    );
    assert_eq!(arguments, vec!["-t".to_owned()]);
}

/// A tree the validator accepts is reloaded onto the running server, which is
/// what closes the case where a killed write left VALID content on disk that
/// the daemon never loaded — a suspension that never took effect.
#[test]
fn a_validated_web_tree_is_reloaded_onto_the_running_server() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = RecordingHost::new(Answer::Accept);

    assert_eq!(
        reconcile_web_tree(&host, adapter),
        WebTreeReconciliation::Applied
    );

    let (program, arguments) = host.reload.lock().unwrap().clone().unwrap();
    assert_eq!(program, adapter.service_manager());
    assert_eq!(
        arguments,
        vec!["reload".to_owned(), adapter.nginx_service().to_owned()]
    );
}

/// A tree the validator rejects is reported as rejected and the start
/// continues: this process is the operator's only remote repair tool, so it
/// never refuses to come up over a file it cannot fix.
#[test]
fn a_rejected_web_tree_is_reported_and_does_not_stop_the_start() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = RecordingHost::new(Answer::RejectValidation);

    assert_eq!(
        reconcile_web_tree(&host, adapter),
        WebTreeReconciliation::Refused
    );
}

/// A reload that refuses on an accepted tree is a different job for an
/// operator — usually a web server that is not running yet — and it is
/// reported as its own outcome rather than folded into the rejection above.
#[test]
fn a_reload_that_refuses_is_reported_apart_from_a_rejected_tree() {
    let adapter = maran_distro::adapter_for(DistroFamily::Debian);
    let host = RecordingHost::new(Answer::RefuseReload);

    assert_eq!(
        reconcile_web_tree(&host, adapter),
        WebTreeReconciliation::NotReloaded
    );
}

/// Every call inside `serve` to a function whose name carries `reconcile_` or
/// `recover_`, in source order.
///
/// Discovered from the source rather than listed, because a list is blind to
/// exactly the addition this is watching for. Comment lines are skipped so the
/// paragraphs in `serve` that DESCRIBE the reconciliation are not mistaken for
/// it.
fn startup_reconcilers() -> Vec<String> {
    let after_signature = SERVER_SOURCE
        .split_once("pub async fn serve(")
        .expect("serve is declared in this file")
        .1;
    let body = after_signature
        .split_once("\n}\n")
        .expect("serve's body ends at a closing brace in the first column")
        .0;

    let mut found = Vec::new();
    for line in body.lines() {
        let trimmed = line.trim_start();
        if trimmed.starts_with("//") {
            continue;
        }
        let bytes = line.as_bytes();
        let mut index = 0;
        while index < bytes.len() {
            let rest = &line[index..];
            let Some(offset) = rest.find("reconcile_").or_else(|| rest.find("recover_")) else {
                break;
            };
            let start = index + offset;
            let end = start
                + line[start..]
                    .find(|c: char| !c.is_alphanumeric() && c != '_')
                    .unwrap_or(line.len() - start);
            if line[end..].starts_with('(') {
                found.push(line[start..end].to_owned());
            }
            index = end;
        }
    }
    found
}

/// The startup pass covers interrupted restores, the web tree and the php-fpm
/// pool trees, and nothing else — which is the still-OPEN part of the
/// config-write midpoint written down as a check instead of as a sentence.
///
/// This test is meant to go RED the day the rest of the gap is closed. Three
/// trees remain unobserved: the SFTP jail mount unit, the FTPS jail mount unit
/// and `vsftpd.conf`, each of which passes a MUTATING command as its
/// `Validator` (`systemctl daemon-reload`, and `systemctl restart` for FTPS), so
/// re-running it at a start is an action and not an observation. When one of the
/// three gains a startup pass, this list grows, this test fails, and whoever
/// closed it must also update the blind-spot list in `reconcile_web_tree`'s doc
/// comment and in its own log line — which is the point: the note must not
/// outlive the gap.
///
/// The list GREW rather than loosened when the php-fpm pool pass landed: the
/// pass's own name is now asserted, in source order, so removing it is as red as
/// adding a fourth reconciler nobody wrote down. Loosening the assertion to
/// "contains" would have made this test agree with a tree that had stopped
/// observing the pools.
///
/// It cannot go silently blind: a scan that stopped finding anything returns
/// an empty list and fails here, rather than agreeing that nothing was added.
#[test]
fn the_only_startup_reconciliations_are_the_restores_the_web_tree_and_the_php_pools() {
    assert_eq!(
        startup_reconcilers(),
        vec![
            "recover_restores".to_owned(),
            "reconcile_web_tree".to_owned(),
            "reconcile_php_pool_trees".to_owned(),
        ],
        "a startup reconciliation was added or removed; update reconcile_web_tree's \
         stated blind spots and its log line, then update this list"
    );
}

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
    fn run(&self, program: &str, arguments: &[&str]) -> Result<CommandOutcome, SafeWriteError> {
        let mut command = vec![program.to_owned()];
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
        ConfigHost::run(self, validator.program, validator.arguments)?;
        if self.validation.0 != 0 {
            return Err(PhpOpError::PoolValidation {
                stderr: self.validation.1.clone(),
            });
        }

        ConfigHost::run(self, reload.program, reload.arguments)?;
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

    let outcomes = reconcile_php_pool_trees(&host, adapter);

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].version, "8.3");
    assert_eq!(outcomes[0].decision, PoolTreeDecision::Applied);
    assert_eq!(
        host.calls(),
        vec![
            vec![adapter.php_fpm_binary("8.3"), "-t".to_owned()],
            vec![
                adapter.service_manager().to_owned(),
                "reload".to_owned(),
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

    let outcomes = reconcile_php_pool_trees(&host, adapter);

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].decision, PoolTreeDecision::Refused);
    assert_eq!(outcomes[0].stderr, "unexpected end of file");
    assert_eq!(
        host.calls(),
        vec![vec![adapter.php_fpm_binary("8.3"), "-t".to_owned()]],
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
