//! Tests for [`AccountDraft`]: an account while its inventory is filled in.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::inventory_fixtures::passwd_entry;
use crate::transfer::inventory::model::account_draft::AccountDraft;

/// A new draft has found nothing yet.
#[test]
fn a_new_draft_has_found_nothing() {
    let draft = AccountDraft::new(passwd_entry("acme", "/home/acme"));

    assert!(draft.sites.is_empty());
    assert!(draft.databases.is_empty());
    assert_eq!(draft.cron_entry_count, 0);
    assert_eq!(draft.mailbox_count, 0);
    assert_eq!(draft.disk_bytes, None);
}

/// A finished draft becomes a record with the home as text and every figure it
/// was given.
#[test]
fn a_finished_draft_becomes_a_record() {
    let mut draft = AccountDraft::new(passwd_entry("acme", "/home/acme"));
    draft.cron_entry_count = 4;
    draft.mailbox_count = 2;
    draft.disk_bytes = Some(10);

    let record = draft.into_record();

    assert_eq!(record.name.as_str(), "acme");
    assert_eq!(record.home, "/home/acme");
    assert_eq!(record.cron_entry_count, 4);
    assert_eq!(record.mailbox_count, 2);
    assert_eq!(record.disk_bytes, Some(10));
}
