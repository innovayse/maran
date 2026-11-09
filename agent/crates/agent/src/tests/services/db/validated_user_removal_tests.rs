//! Tests for the login a `DropDatabaseUser` request is rebuilt into.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::validated_user_removal;
use crate::proto::ErrorCode;

/// The account the panel authorised, as the handler hands it on.
fn alice() -> AccountName {
    AccountName::parse("alice").expect("a valid account name")
}

#[test]
fn the_login_is_rebuilt_under_the_account_the_panel_authorised() {
    let user = validated_user_removal(&alice(), "reader").expect("valid");

    assert_eq!(user.as_str(), "alice_reader");
}

#[test]
fn a_suffix_naming_another_tenant_is_refused_rather_than_forwarded() {
    let refused = validated_user_removal(&alice(), "bob_reader").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_suffix_is_refused_rather_than_treated_as_every_login() {
    let refused = validated_user_removal(&alice(), "").expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_wildcard_or_quote_in_the_suffix_is_refused() {
    for suffix in ["%", "reader'", "re ader", "reader;"] {
        let refused = validated_user_removal(&alice(), suffix).expect_err("must refuse");

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{suffix}");
    }
}
