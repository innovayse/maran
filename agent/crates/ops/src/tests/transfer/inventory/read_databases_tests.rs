//! Tests for [`read_databases`]: the third step of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::RemoteCommand;
use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{RecordingSink, count_of, passwd_entry};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::account_draft::AccountDraft;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_databases::read_databases;

/// Tab-separated names and sizes (synthetic).
const LISTING: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/mysql_listing.txt");

/// Two drafts: `acme` and `shopsite`.
fn drafts() -> Vec<AccountDraft> {
    vec![
        AccountDraft::new(passwd_entry("acme", "/home/acme")),
        AccountDraft::new(passwd_entry("shopsite", "/home/shopsite")),
    ]
}

/// Databases are attached to their accounts; the ones that belong to nobody and
/// the one with a bad shape are three warnings without text.
#[test]
fn databases_are_attached_and_the_rest_are_name_warnings() {
    let shell = FakeSourceShell::new().answering(RemoteCommand::ListDatabases, LISTING);
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_databases(&mut run, &mut drafts).unwrap();

    assert_eq!(drafts[0].databases.len(), 2);
    assert_eq!(drafts[0].databases[0].name.as_str(), "acme_wp");
    assert_eq!(drafts[1].databases.len(), 1);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::NameRefused), 3);
    assert!(
        sink.warnings
            .iter()
            .all(|warning| warning.subject().is_empty())
    );
}

/// A listing that cannot be read is one DatabasesUnreadable and no databases.
#[test]
fn an_unreadable_listing_is_one_warning() {
    let shell = FakeSourceShell::new().exiting(RemoteCommand::ListDatabases, 1, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_databases(&mut run, &mut drafts).unwrap();

    assert!(drafts.iter().all(|draft| draft.databases.is_empty()));
    assert_eq!(sink.warnings.len(), 1);
    assert_eq!(
        sink.warnings[0].kind(),
        SourceWarningKind::DatabasesUnreadable
    );
}

/// An empty listing is a server with no databases: no warning.
#[test]
fn an_empty_listing_is_not_a_warning() {
    let shell = FakeSourceShell::new().answering(RemoteCommand::ListDatabases, "");
    let mut sink = RecordingSink::default();
    let mut drafts = drafts();
    let mut run = InventoryRun::new(&shell, &mut sink);

    read_databases(&mut run, &mut drafts).unwrap();

    assert!(sink.warnings.is_empty());
}
