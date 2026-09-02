//! Tests for the `database_name` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::db::database_name_error::DatabaseNameError;
use crate::validation::system::name::AccountName;

use super::DatabaseName;

fn account() -> AccountName {
    AccountName::parse("acme").expect("a valid account name")
}

#[test]
fn a_valid_request_is_prefixed_with_the_owning_account() {
    let name = DatabaseName::for_account(&account(), "shop2024").expect("a valid request");

    assert_eq!(name.as_str(), "acme_shop2024");
}

#[test]
fn an_underscore_in_the_request_is_rejected_so_ownership_cannot_be_forged() {
    let result = DatabaseName::for_account(&account(), "bob_secrets");

    assert!(matches!(
        result,
        Err(DatabaseNameError::UnexpectedCharacter { character: '_' })
    ));
}

#[test]
fn an_empty_request_is_rejected() {
    assert!(matches!(
        DatabaseName::for_account(&account(), ""),
        Err(DatabaseNameError::Empty)
    ));
}

#[test]
fn a_request_that_overflows_mysqls_identifier_limit_is_rejected() {
    // "acme" + "_" + 60 = 65 bytes, one past MySQL's 64-byte identifier limit.
    let result = DatabaseName::for_account(&account(), &"a".repeat(60));

    assert!(matches!(
        result,
        Err(DatabaseNameError::TooLong { length: 65 })
    ));
}

#[test]
fn every_character_that_would_break_out_of_the_identifier_quoting_is_rejected() {
    // ``CREATE DATABASE `name` `` cannot bind an identifier, so this value is interpolated.
    // What makes that safe is that none of these can be held — not that anything escapes them.
    // Without this test the allow-list could be widened by one character unnoticed.
    for requested in [
        "shop; DROP",
        "shop`",
        "shop'",
        "shop\"",
        "shop\\",
        "shop ",
        "shop\n",
        "Shop",
        "shop-1",
    ] {
        assert!(
            DatabaseName::for_account(&account(), requested).is_err(),
            "{requested:?} must be rejected"
        );
    }
}

#[test]
fn a_request_that_exactly_fills_mysqls_identifier_limit_is_accepted() {
    // "acme" + "_" + 59 = 64 bytes, exactly MySQL's limit. Without this the ceiling's `>`
    // could become `>=` and refuse a legitimate name with no named test noticing.
    let name = DatabaseName::for_account(&account(), &"a".repeat(59)).expect("a valid request");

    assert_eq!(name.as_str().len(), 64);
}
