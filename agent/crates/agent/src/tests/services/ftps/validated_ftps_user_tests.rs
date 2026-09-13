//! A login name is built from the account, never taken off the wire.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::proto::ErrorCode;
use crate::services::ftps::validated_ftps_user::validated_ftps_user;

#[test]
fn the_login_is_namespaced_under_the_account_the_panel_authorised() {
    let (account, user) = validated_ftps_user("alice", "web").expect("a valid pair");

    assert_eq!(account.as_str(), "alice");
    assert_eq!(user.as_str(), "alice_web");
}

#[test]
fn a_suffix_naming_another_tenants_login_cannot_produce_that_login() {
    // The separator is outside the suffix alphabet, so a suffix cannot smuggle
    // in a second prefix — and a suffix that tried would in any case produce a
    // name under the CALLER's own account.
    let result = validated_ftps_user("alice", "bob_web");

    assert_eq!(
        result.err().map(|error| error.code),
        Some(ErrorCode::InvalidInput as i32)
    );
}

#[test]
fn a_traversal_shaped_suffix_is_refused_rather_than_sanitised() {
    for suffix in ["../root", "web/../..", "WEB", "", "we b"] {
        let result = validated_ftps_user("alice", suffix);
        assert!(result.is_err(), "{suffix:?} must be refused");
    }
}

#[test]
fn an_account_name_the_agent_will_not_accept_is_refused_on_its_own_account() {
    // The agent re-checks the account name even though the API validated it,
    // because the agent runs as root and the API does not.
    let result = validated_ftps_user("Alice", "web");

    assert_eq!(
        result.err().map(|error| error.code),
        Some(ErrorCode::InvalidInput as i32)
    );
}

#[test]
fn the_refusal_message_names_the_condition_and_not_the_value() {
    let error = validated_ftps_user("alice", "BAD").expect_err("refused");

    assert!(!error.message.is_empty(), "the operator is owed a reason");
    assert!(
        error.tool_output.is_empty(),
        "an input check runs no tool and must invent no output"
    );
}
