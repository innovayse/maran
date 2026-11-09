//! Tests for the typed input a `CreateDatabaseUser` request becomes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::validated_user_creation;
use crate::proto::ErrorCode;

/// The account the panel authorised, as the handler hands it on.
fn alice() -> AccountName {
    AccountName::parse("alice").expect("a valid account name")
}

#[test]
fn the_login_is_rebuilt_under_the_account_and_the_password_is_carried_through() {
    let input = validated_user_creation(&alice(), "reader", "Str0ng-pass.word=+_").expect("valid");

    assert_eq!(input.user.as_str(), "alice_reader");
    assert_eq!(input.password.as_str(), "Str0ng-pass.word=+_");
}

#[test]
fn a_suffix_naming_another_tenant_is_refused_rather_than_forwarded() {
    let refused =
        validated_user_creation(&alice(), "bob_admin", "Str0ng-pass").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_suffix_is_refused() {
    let refused = validated_user_creation(&alice(), "", "Str0ng-pass").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_login_name_past_the_servers_limit_is_refused() {
    let refused =
        validated_user_creation(&alice(), &"a".repeat(40), "Str0ng-pass").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_password_carrying_a_quote_is_refused_and_never_echoed() {
    let refused = validated_user_creation(&alice(), "reader", "pw'; DROP USER x; --")
        .expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(!refused.message.contains("DROP USER"));
}

#[test]
fn an_empty_password_is_refused() {
    let refused = validated_user_creation(&alice(), "reader", "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
