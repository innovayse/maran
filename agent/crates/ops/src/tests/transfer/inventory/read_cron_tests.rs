//! Tests for [`read_cron`]: the fourth step of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::TransferOpError;
use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{
    RecordingSink, count_of, crontab_of, passwd_entry,
};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::account_draft::AccountDraft;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_cron::read_cron;

/// A crontab with four entries (synthetic).
const BASIC: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/crontab_basic.txt");

/// A crontab with one entry and three lines that are not (synthetic).
const HOSTILE: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/crontab_hostile.txt");

/// Three drafts: `acme`, `shopsite` and `deploy`.
fn drafts() -> Vec<AccountDraft> {
    vec![
        AccountDraft::new(passwd_entry("acme", "/home/acme")),
        AccountDraft::new(passwd_entry("shopsite", "/home/shopsite")),
        AccountDraft::new(passwd_entry("deploy", "/opt/deploy")),
    ]
}

/// Entries are counted, status 1 is no crontab and says nothing, and any other
/// failing status is a warning naming the account.
#[test]
fn entries_are_counted_and_only_a_real_failure_is_a_warning() {
    let shell = FakeSourceShell::new()
        .answering(crontab_of("acme"), BASIC)
        .exiting(crontab_of("shopsite"), 1, "")
        .exiting(crontab_of("deploy"), 127, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_cron(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].cron_entry_count, 4);
    assert_eq!(drafts[1].cron_entry_count, 0);
    assert_eq!(sink.warnings.len(), 1);
    assert_eq!(sink.warnings[0].kind(), SourceWarningKind::CronUnreadable);
    assert_eq!(sink.warnings[0].subject(), "deploy");
}

/// Lines that are not entries cost one CronUnreadable naming the account, and
/// the real entry is still counted.
#[test]
fn lines_that_are_not_entries_are_one_warning() {
    let shell = FakeSourceShell::new()
        .answering(crontab_of("acme"), HOSTILE)
        .exiting(crontab_of("shopsite"), 1, "")
        .exiting(crontab_of("deploy"), 1, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_cron(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].cron_entry_count, 1);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::CronUnreadable),
        1
    );
    assert_eq!(sink.warnings[0].subject(), "acme");
}

/// A crontab command that crossed its bound is a Truncated warning, no entries,
/// and the other accounts are still read.
#[test]
fn a_bounded_crontab_is_truncated_and_the_next_is_read() {
    let shell = FakeSourceShell::new()
        .bounded(crontab_of("acme"))
        .answering(crontab_of("shopsite"), BASIC)
        .exiting(crontab_of("deploy"), 1, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_cron(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].cron_entry_count, 0);
    assert_eq!(drafts[1].cron_entry_count, 4);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}

/// A lost connection ends the step.
#[test]
fn a_connection_failure_ends_the_step() {
    let shell = FakeSourceShell::new().unreachable(crontab_of("acme"));
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let result = read_cron(&mut run, &mut drafts);

    assert!(matches!(result, Err(TransferOpError::Unreachable)));
}
