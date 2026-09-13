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

#[test]
fn every_name_the_constructor_accepts_decodes_back_to_itself() {
    // The round-trip property: construction and its inverse must agree for EVERY accepted
    // input, not only for the tidy one the worked example uses. The accounts below are the
    // cases that could break an `rsplit` — a plain name, an account whose own name contains
    // the separator, and one that is mostly separators — crossed with the whole accepted
    // alphabet of the requested half.
    for account_name in ["acme", "alice_bob", "a_b_c_d"] {
        let owner = AccountName::parse(account_name).expect("a valid account name");

        for requested in ["shop", "a", "shop2024", "x1", "zzzzzzzzzz"] {
            let built = DatabaseName::for_account(&owner, requested).expect("a valid request");

            let decoded = DatabaseName::decode(&owner, built.as_str());

            assert_eq!(
                decoded.map(|name| name.as_str().to_owned()),
                Some(built.as_str().to_owned()),
                "{account_name} / {requested} must round-trip"
            );
        }
    }
}

#[test]
fn a_name_that_exactly_fills_the_limit_still_round_trips() {
    // The boundary the constructor accepts is the boundary the decoder must too: `decode`
    // rebuilds through `for_account`, so a ceiling that disagreed by one byte would drop the
    // longest names an account owns and nothing else.
    let built =
        DatabaseName::for_account(&account(), &"a".repeat(64 - 5)).expect("a valid request");

    let decoded =
        DatabaseName::decode(&account(), built.as_str()).expect("the longest name round-trips");

    assert_eq!(decoded.as_str().len(), 64);
}

#[test]
fn another_accounts_name_does_not_decode() {
    // `alice_` is a prefix of `alice_bob_secrets`, so a prefix scan would hand this
    // account another tenant's name. Splitting at the LAST separator and comparing the whole
    // account is what refuses it.
    let other = AccountName::parse("alice_bob").expect("a valid account name");
    let theirs = DatabaseName::for_account(&other, "secrets").expect("a valid request");
    let alice = AccountName::parse("alice").expect("a valid account name");

    assert!(DatabaseName::decode(&alice, theirs.as_str()).is_none());
}

#[test]
fn a_name_that_was_never_built_by_this_agent_decodes_to_nothing() {
    // `decode` refuses rather than guessing: these reach it from the server's own listings,
    // and a plausible-looking answer for a name outside the convention is what would put
    // another tenant's row into a listing or a deletion.
    for candidate in [
        "",                // nothing at all
        "acme",            // no separator: the account's own name is not a prefixed name
        "_",               // the separator alone: both halves empty
        "___",             // all separators, which the requested half may not contain
        "acme_",           // the account, then nothing requested
        "_shop",           // a requested half with no account in front
        "other_shop",      // a different account entirely
        "acme_Shop",       // outside the alphabet, so this agent never created it
        "acme_shop_extra", // decodes to account `acme_shop`, which is not `acme`
    ] {
        assert!(
            DatabaseName::decode(&account(), candidate).is_none(),
            "{candidate:?} must not decode"
        );
    }
}
