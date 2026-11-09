//! Tests for the scope a restore is narrowed to.
//!
//! The proposition of this file: a caller never names a path. A database must
//! decode to the account being restored, a site arrives as a domain and the
//! directory it occupies is derived by the agent, so a scope cannot aim a
//! restore at anything but the account's own database or the place a site is
//! served from. Each refusal has its acceptance beside it (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::backup::RestoreScope;

use super::validated_scope;
use crate::proto::{ErrorCode, RestoreScope as WireRestoreScope, restore_scope};

/// The account every scope in this file is restored into.
fn account() -> AccountName {
    AccountName::parse("alice").expect("a valid account name")
}

/// A wire scope naming `target`.
fn scope(target: restore_scope::Target) -> WireRestoreScope {
    WireRestoreScope {
        target: Some(target),
    }
}

/// No scope at all is the whole account: the only meaning a request from before
/// the field existed can have.
#[test]
fn an_absent_scope_is_the_whole_account() {
    assert_eq!(validated_scope(None, &account()), Ok(RestoreScope::Whole));
}

/// A scope message with no target set is the whole account too, not an error and
/// not an empty restore.
#[test]
fn a_scope_naming_nothing_is_the_whole_account() {
    assert_eq!(
        validated_scope(Some(&WireRestoreScope { target: None }), &account()),
        Ok(RestoreScope::Whole)
    );
}

/// A database of the account is accepted under the panel's own spelling.
#[test]
fn a_database_of_the_account_is_accepted() {
    let wire = scope(restore_scope::Target::DatabaseName("alice_shop".to_owned()));

    let scope = validated_scope(Some(&wire), &account()).unwrap();

    match scope {
        RestoreScope::Database(name) => assert_eq!(name.as_str(), "alice_shop"),
        other => panic!("expected a database scope, got {other:?}"),
    }
}

/// A neighbour's database, a system database and an empty name are all refused
/// as invalid input: a restore cannot be aimed at a database the account does not
/// own.
#[test]
fn a_database_the_account_does_not_own_is_refused() {
    for name in ["bob_shop", "mysql", "information_schema", "alice", ""] {
        let wire = scope(restore_scope::Target::DatabaseName(name.to_owned()));

        let error = validated_scope(Some(&wire), &account()).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{name:?}");
    }
}

/// A domain names the directory the agent put the site in, and nothing the caller
/// wrote reaches the path.
#[test]
fn a_site_domain_becomes_the_directory_site_creation_used() {
    let wire = scope(restore_scope::Target::SiteDomain(
        "Shop.Example.COM".to_owned(),
    ));

    let scope = validated_scope(Some(&wire), &account()).unwrap();

    assert_eq!(
        scope,
        RestoreScope::Directory(RelativePath::parse("sites/shop.example.com").unwrap()),
        "lowercased, and under the sites directory"
    );
}

/// Anything that is not a domain is refused before a path is built from it:
/// traversal, separators, control characters and the empty string.
#[test]
fn a_name_that_is_not_a_domain_is_refused() {
    for name in [
        "",
        "..",
        "../etc",
        "a/b.example.com",
        "shop.example.com/..",
        "shop.example.com\n",
        "shop example.com",
        "-shop.example.com",
    ] {
        let wire = scope(restore_scope::Target::SiteDomain(name.to_owned()));

        let error = validated_scope(Some(&wire), &account()).unwrap_err();

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{name:?}");
    }
}
