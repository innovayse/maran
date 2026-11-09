//! Tests for the typed input a `SetDatabaseUserPrivileges` request becomes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::validated_privilege_change;
use crate::proto::{DatabasePrivilege as WirePrivilege, ErrorCode};

/// The account the panel authorised, as the handler hands it on.
fn alice() -> AccountName {
    AccountName::parse("alice").expect("a valid account name")
}

#[test]
fn both_names_are_rebuilt_under_the_account_and_the_set_is_carried_through() {
    let change = validated_privilege_change(
        &alice(),
        "reader",
        "shop",
        &[WirePrivilege::Select as i32, WirePrivilege::Execute as i32],
    )
    .expect("valid");

    assert_eq!(change.user.as_str(), "alice_reader");
    assert_eq!(change.database.as_str(), "alice_shop");
    assert_eq!(change.privileges.sql_list(), "SELECT, EXECUTE");
}

#[test]
fn the_login_and_the_database_are_independent_names() {
    let change = validated_privilege_change(&alice(), "blog", "shop", &[]).expect("valid");

    assert_eq!(change.user.as_str(), "alice_blog");
    assert_eq!(change.database.as_str(), "alice_shop");
    assert!(change.privileges.is_empty());
}

#[test]
fn a_database_suffix_naming_another_tenant_is_refused() {
    let refused =
        validated_privilege_change(&alice(), "reader", "bob_shop", &[1]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_login_suffix_naming_another_tenant_is_refused() {
    let refused =
        validated_privilege_change(&alice(), "bob_reader", "shop", &[1]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_database_or_login_suffix_is_refused() {
    for (login, database) in [("", "shop"), ("reader", "")] {
        let refused =
            validated_privilege_change(&alice(), login, database, &[1]).expect_err("must refuse");

        assert_eq!(
            refused.code,
            ErrorCode::InvalidInput as i32,
            "{login}/{database}"
        );
    }
}

#[test]
fn an_unspecified_privilege_refuses_the_change() {
    let refused =
        validated_privilege_change(&alice(), "reader", "shop", &[0]).expect_err("must refuse");

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
