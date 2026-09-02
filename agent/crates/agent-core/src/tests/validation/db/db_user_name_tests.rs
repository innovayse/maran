//! Tests for the `db_user_name` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::db::db_user_name_error::DbUserNameError;
use crate::validation::system::name::AccountName;

use super::DbUserName;

fn account() -> AccountName {
    AccountName::parse("acme").expect("a valid account name")
}

#[test]
fn a_valid_request_is_prefixed_with_the_owning_account() {
    let name = DbUserName::for_account(&account(), "shop").expect("a valid request");

    assert_eq!(name.as_str(), "acme_shop");
}

#[test]
fn an_underscore_in_the_request_is_rejected_so_ownership_cannot_be_forged() {
    let result = DbUserName::for_account(&account(), "bob_admin");

    assert!(matches!(
        result,
        Err(DbUserNameError::UnexpectedCharacter { character: '_' })
    ));
}

#[test]
fn an_empty_request_is_rejected() {
    assert!(matches!(
        DbUserName::for_account(&account(), ""),
        Err(DbUserNameError::Empty)
    ));
}

#[test]
fn a_request_that_overflows_mysqls_user_limit_is_rejected() {
    // "acme" + "_" + 28 = 33 bytes, one past MySQL's 32-byte user-name limit.
    let result = DbUserName::for_account(&account(), &"a".repeat(28));

    assert!(matches!(
        result,
        Err(DbUserNameError::TooLong { length: 33 })
    ));
}

#[test]
fn every_character_that_would_break_out_of_the_user_quoting_is_rejected() {
    // `CREATE USER '<name>'@'localhost'` cannot bind the name, so it is interpolated. Without
    // this test the allow-list could be widened by one character unnoticed.
    for requested in [
        "shop'@'%", "shop`", "shop\"", "shop\\", "shop ", "shop\n", "Shop", "shop-1",
    ] {
        assert!(
            DbUserName::for_account(&account(), requested).is_err(),
            "{requested:?} must be rejected"
        );
    }
}

#[test]
fn a_request_that_exactly_fills_mysqls_user_limit_is_accepted() {
    // "acme" + "_" + 27 = 32 bytes, exactly MySQL's user-name limit. Without this the
    // ceiling's `>` could become `>=` with no named test noticing.
    let name = DbUserName::for_account(&account(), &"a".repeat(27)).expect("a valid request");

    assert_eq!(name.as_str().len(), 32);
}

#[test]
fn a_request_containing_digits_is_accepted() {
    // The other accepted-path tests here use letters only, so without this the digit half of
    // the allow-list could be deleted and no named test would go red.
    let name = DbUserName::for_account(&account(), "shop2024").expect("a valid request");

    assert_eq!(name.as_str(), "acme_shop2024");
}
