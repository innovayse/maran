//! Tests for the startup validation of the web configuration tree.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints"): a failed unwrap here is the test reporting, and
//! the workspace lints are denied rather than warned, so the exemption is
//! declared file by file as every other test file in this tree declares it.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Mutex;

use maran_distro::DistroFamily;
use maran_ops::safe_write::model::{Reload, Validator};
use maran_ops::sites::{LogSink, LogTailRequest, SiteMaintenanceHost, SitesOpError, TailEnd};

use super::*;
use crate::startup::web_tree_reconciliation::WebTreeReconciliation;

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
            validator.program.to_string(),
            validator
                .arguments
                .iter()
                .map(|a| (*a).to_owned())
                .collect(),
        ));
        *self.reload.lock().unwrap() = Some((
            reload.program.to_string(),
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
    assert_eq!(program, adapter.nginx_binary().as_str());
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
    assert_eq!(program, adapter.service_manager().as_str());
    assert_eq!(
        arguments,
        vec![
            "reload".to_owned(),
            "--".to_owned(),
            adapter.nginx_service().to_owned()
        ]
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
