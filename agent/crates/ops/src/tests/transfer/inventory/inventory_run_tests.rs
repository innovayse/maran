//! Tests for [`InventoryRun`]: the commands and events of one read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{RecordingSink, count_of};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::inventory_limits::InventoryLimits;
use crate::transfer::inventory::model::inventory_step::InventoryStep;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::model::source_warning_record::SourceWarningRecord;
use crate::transfer::{RemoteCommand, TransferOpError};

/// A warning of a kind that is not Truncated.
fn warning() -> SourceWarningRecord {
    SourceWarningRecord::about_nothing(SourceWarningKind::NameRefused)
}

/// The warnings up to the bound are sent as they are.
#[test]
fn warnings_up_to_the_bound_are_all_sent() {
    let shell = FakeSourceShell::new();
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    for _ in 0..InventoryLimits::MAX_WARNINGS {
        run.warn(warning());
    }
    let summary = run.summary();

    assert_eq!(summary.warning_count, InventoryLimits::MAX_WARNINGS);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 0);
}

/// The warning after the bound becomes one Truncated and everything after it is
/// dropped.
#[test]
fn warnings_past_the_bound_become_one_truncated() {
    let shell = FakeSourceShell::new();
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    for _ in 0..(InventoryLimits::MAX_WARNINGS * 3) {
        run.warn(warning());
    }
    let summary = run.summary();

    assert_eq!(
        sink.warnings.len(),
        InventoryLimits::MAX_WARNINGS as usize + 1
    );
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
    assert_eq!(
        sink.warnings.last().unwrap().kind(),
        SourceWarningKind::Truncated
    );
    assert_eq!(summary.warning_count, InventoryLimits::MAX_WARNINGS + 1);
}

/// A step is announced while the sink listens and refused once it does not.
#[test]
fn a_step_is_not_announced_to_a_closed_sink() {
    let shell = FakeSourceShell::new();
    let mut sink = RecordingSink {
        closes_after_steps: Some(1),
        ..RecordingSink::default()
    };
    let mut run = InventoryRun::new(&shell, &mut sink);

    assert!(run.begin(InventoryStep::Accounts));
    assert!(!run.begin(InventoryStep::Sites));
    assert_eq!(sink.steps, [InventoryStep::Accounts]);
}

/// A command that crossed its bound is a Truncated warning and no answer; the
/// read goes on.
#[test]
fn a_bounded_command_is_a_warning_and_no_answer() {
    let shell = FakeSourceShell::new().bounded(RemoteCommand::ListDatabases);
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let outcome = run.outcome(&RemoteCommand::ListDatabases).unwrap();

    assert!(outcome.is_none());
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}

/// A connection failure ends the read: it is an error and not a warning.
#[test]
fn a_connection_failure_is_an_error() {
    let shell = FakeSourceShell::new().unreachable(RemoteCommand::ListPasswd);
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let outcome = run.outcome(&RemoteCommand::ListPasswd);

    assert!(matches!(outcome, Err(TransferOpError::Unreachable)));
    assert!(sink.warnings.is_empty());
}

/// A non-zero status sends the failure warning and gives no text; status zero is
/// the inverse control and gives the text without a warning.
#[test]
fn text_needs_a_zero_status() {
    let shell = FakeSourceShell::new()
        .exiting(RemoteCommand::ListDatabases, 1, "ignored\n")
        .answering(RemoteCommand::ListPhpVersions, "8.2\n");
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let failed = run
        .text(
            &RemoteCommand::ListDatabases,
            SourceWarningRecord::about_nothing(SourceWarningKind::DatabasesUnreadable),
        )
        .unwrap();
    let worked = run
        .text(&RemoteCommand::ListPhpVersions, warning())
        .unwrap();

    assert!(failed.is_none());
    assert_eq!(worked.unwrap().text, "8.2\n");
    assert_eq!(sink.warnings.len(), 1);
    assert_eq!(
        sink.warnings[0].kind(),
        SourceWarningKind::DatabasesUnreadable
    );
}

/// Output over the bound is cut to whole lines and sends Truncated.
#[test]
fn text_over_the_bound_is_cut_and_marked() {
    let big = "line\n".repeat(InventoryLimits::MAX_OUTPUT_BYTES / 5 + 10);
    let shell = FakeSourceShell::new().answering(RemoteCommand::ListPasswd, &big);
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let text = run
        .text(&RemoteCommand::ListPasswd, warning())
        .unwrap()
        .unwrap();

    assert!(text.truncated);
    assert!(text.text.len() <= InventoryLimits::MAX_OUTPUT_BYTES);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}

/// A listing accepts status 1 (a `find` over a missing directory) and keeps what
/// it printed; status 127 is the inverse control and is a failure.
#[test]
fn a_listing_accepts_status_one_but_not_a_missing_tool() {
    let shell = FakeSourceShell::new()
        .exiting(
            RemoteCommand::ListNginxVhostFiles,
            1,
            "/etc/nginx/conf.d/a.conf\n",
        )
        .exiting(RemoteCommand::ListApacheVhostFiles, 127, "");
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let nginx = run
        .listing(&RemoteCommand::ListNginxVhostFiles, warning())
        .unwrap();
    let apache = run
        .listing(&RemoteCommand::ListApacheVhostFiles, warning())
        .unwrap();

    assert_eq!(nginx.unwrap().text, "/etc/nginx/conf.d/a.conf\n");
    assert!(apache.is_none());
    assert_eq!(sink.warnings.len(), 1);
}
