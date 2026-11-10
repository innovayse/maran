//! Tests for [`read_disk`]: the last step of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{
    RecordingSink, count_of, mailboxes_of, passwd_entry, usage_of,
};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::account_draft::AccountDraft;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_disk::read_disk;

/// Two drafts: `acme` and `shopsite`.
fn drafts() -> Vec<AccountDraft> {
    vec![
        AccountDraft::new(passwd_entry("acme", "/home/acme")),
        AccountDraft::new(passwd_entry("shopsite", "/home/shopsite")),
    ]
}

/// Sizes and mailbox counts are read; mail found is a MailNotMigrated naming
/// the account, and no mail is the inverse control: no warning.
#[test]
fn sizes_and_mailboxes_are_read_and_mail_is_a_warning() {
    let shell = FakeSourceShell::new()
        .answering(usage_of("/home/acme"), "2048\t/home/acme\n")
        .answering(mailboxes_of("/home/acme"), "3\n")
        .answering(usage_of("/home/shopsite"), "10\t/home/shopsite\n")
        .answering(mailboxes_of("/home/shopsite"), "0\n");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_disk(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].disk_bytes, Some(2_097_152));
    assert_eq!(drafts[0].mailbox_count, 3);
    assert_eq!(drafts[1].disk_bytes, Some(10_240));
    assert_eq!(drafts[1].mailbox_count, 0);
    assert_eq!(sink.warnings.len(), 1);
    assert_eq!(sink.warnings[0].kind(), SourceWarningKind::MailNotMigrated);
    assert_eq!(sink.warnings[0].subject(), "acme");
}

/// A size from a command that failed, or an unreadable figure, is not a size:
/// the account keeps none and a DiskUnreadable names it.
#[test]
fn a_failed_measurement_is_a_warning_and_no_size() {
    let shell = FakeSourceShell::new()
        .exiting(usage_of("/home/acme"), 1, "9999\t/home/acme\n")
        .answering(mailboxes_of("/home/acme"), "0\n")
        .answering(usage_of("/home/shopsite"), "not a figure\n")
        .answering(mailboxes_of("/home/shopsite"), "0\n");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_disk(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].disk_bytes, None);
    assert_eq!(drafts[1].disk_bytes, None);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::DiskUnreadable),
        2
    );
}

/// A mailbox count that cannot be read is a DiskUnreadable and no count.
#[test]
fn an_unreadable_mailbox_count_is_a_warning() {
    let shell = FakeSourceShell::new()
        .answering(usage_of("/home/acme"), "1\t/home/acme\n")
        .answering(mailboxes_of("/home/acme"), "lots\n")
        .answering(usage_of("/home/shopsite"), "1\t/home/shopsite\n")
        .answering(mailboxes_of("/home/shopsite"), "0\n");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_disk(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].mailbox_count, 0);
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::DiskUnreadable),
        1
    );
    assert_eq!(
        count_of(&sink.warnings, SourceWarningKind::MailNotMigrated),
        0
    );
}
