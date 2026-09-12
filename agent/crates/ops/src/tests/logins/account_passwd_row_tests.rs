//! Finding the hosting account's own row, and never a neighbour's.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::system_account::SystemAccount;
use maran_agent_core::validation::system::name::AccountName;

use crate::logins::account_passwd_row::account_passwd_row;
use crate::logins::logins_error::LoginsError;

/// One password-database row, as the host's file would hold it.
fn row(name: &str, uid: u32, home: &str) -> SystemAccount {
    SystemAccount {
        name: name.to_owned(),
        uid,
        gid: uid,
        home: home.to_owned(),
    }
}

#[test]
fn the_accounts_own_row_is_the_one_whose_name_matches_exactly() {
    let rows = [
        row("alice", 1001, "/home/alice"),
        row("bob", 1002, "/home/bob"),
    ];

    let found = account_passwd_row(&rows, &AccountName::parse("alice").expect("valid"))
        .expect("the row is there");

    assert_eq!(found.uid, 1001);
}

#[test]
fn a_row_whose_name_merely_begins_with_the_account_is_not_the_accounts_row() {
    // The collision that has already produced a cross-tenant destructive
    // operation in this tree: `alice_bob` is both a possible account of its own
    // and the shape of a login of the account `alice`. A prefix match would hand
    // back the wrong tenant's uid — and this function's second caller kills
    // processes by that uid.
    let rows = [row("alice_bob", 1002, "/home/alice_bob")];

    let refused = account_passwd_row(&rows, &AccountName::parse("alice").expect("valid"));

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn a_database_holding_no_row_for_the_account_is_refused_rather_than_defaulted() {
    // The vacuity guard: the alternative to refusing is inventing a uid, and
    // every uid this function could invent belongs to somebody.
    let rows = [row("bob", 1002, "/home/bob")];

    let refused = account_passwd_row(&rows, &AccountName::parse("alice").expect("valid"));

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}
