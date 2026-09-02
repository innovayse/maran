//! Tests for the two names a `DropDatabase` is rebuilt into.
//!
//! The whole point of this unit is that a request cannot name another tenant's
//! database or another tenant's user: both halves are rebuilt from the account
//! the panel authorised, and neither type has a constructor that takes a whole
//! name. These tests pin that, and pin that the two halves stay independent —
//! a drop that derived the user from the database would either strand a live
//! credential or remove one belonging to a different database.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_removal;
use crate::proto::ErrorCode;

#[test]
fn both_names_are_rebuilt_under_the_account_the_panel_authorised() {
    let (database, user) = validated_removal("alice", "shop", "reader").expect("valid");

    assert_eq!(database.as_str(), "alice_shop");
    assert_eq!(user.as_str(), "alice_reader");
}

#[test]
fn the_user_is_taken_from_its_own_field_and_never_derived_from_the_database() {
    // The customer names the two independently. A drop that guessed the user
    // from the database would remove the wrong credential, or none.
    let (database, user) = validated_removal("alice", "shop", "blog").expect("valid");

    assert_eq!(database.as_str(), "alice_shop");
    assert_eq!(user.as_str(), "alice_blog");
    assert_ne!(database.as_str(), user.as_str());
}

#[test]
fn a_suffix_naming_another_tenant_produces_a_name_under_the_callers_own_account() {
    // `bob_secrets` cannot smuggle in a second prefix: the separator is outside
    // the suffix alphabet, so this is refused rather than becoming a name that
    // reads as bob's in every listing an operator will ever look at.
    let refused = validated_removal("alice", "bob_secrets", "reader").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused_before_anything_is_built() {
    let refused = validated_removal("", "shop", "reader").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_user_suffix_is_refused_rather_than_meaning_drop_the_database_only() {
    let refused = validated_removal("alice", "shop", "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
