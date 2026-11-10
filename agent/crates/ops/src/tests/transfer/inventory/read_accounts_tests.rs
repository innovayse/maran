//! Tests for [`read_accounts`]: the first step of an inventory read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::fake_source_shell::FakeSourceShell;
use crate::transfer::inventory::inventory_fixtures::{RecordingSink, count_of};
use crate::transfer::inventory::inventory_run::InventoryRun;
use crate::transfer::inventory::model::source_warning_kind::SourceWarningKind;
use crate::transfer::inventory::read_accounts::read_accounts;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Hand-written account database (synthetic).
const PASSWD: &str =
    include_str!("../../../../tests/fixtures/transfer/synthetic_inventory/passwd_hosting.txt");

/// A hosting source yields a draft per hosting account.
#[test]
fn a_draft_is_started_for_each_hosting_account() {
    let shell = FakeSourceShell::new().answering(RemoteCommand::ListPasswd, PASSWD);
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let drafts = read_accounts(&mut run).unwrap();

    let names: Vec<&str> = drafts.iter().map(|d| d.entry.name.as_str()).collect();
    assert_eq!(names, ["acme", "shopsite", "deploy"]);
    assert!(sink.warnings.is_empty());
}

/// An account list that did not run, or exited non-zero, ends the read.
#[test]
fn a_failed_account_list_ends_the_read() {
    for shell in [
        FakeSourceShell::new().exiting(RemoteCommand::ListPasswd, 2, ""),
        FakeSourceShell::new().bounded(RemoteCommand::ListPasswd),
        FakeSourceShell::new(),
    ] {
        let mut sink = RecordingSink::default();
        let mut run = InventoryRun::new(&shell, &mut sink);

        let result = read_accounts(&mut run);

        assert!(matches!(result, Err(TransferOpError::InvalidRemoteOutput)));
    }
}

/// A listing past the output bound is read as far as it goes and marked.
#[test]
fn an_oversize_listing_is_read_as_far_as_it_goes() {
    let mut listing = String::from(PASSWD);
    // Comment lines of about a hundred bytes: past the byte bound long before the
    // line bound, so only the output bound is what cuts the listing.
    listing.push_str(&format!("# {}\n", "p".repeat(100)).repeat(30_000));
    let shell = FakeSourceShell::new().answering(RemoteCommand::ListPasswd, &listing);
    let mut sink = RecordingSink::default();
    let mut run = InventoryRun::new(&shell, &mut sink);

    let drafts = read_accounts(&mut run).unwrap();

    assert_eq!(drafts.len(), 3);
    assert_eq!(count_of(&sink.warnings, SourceWarningKind::Truncated), 1);
}
