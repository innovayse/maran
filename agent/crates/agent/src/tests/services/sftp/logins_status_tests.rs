//! Tests for the wire code every file-transfer login failure travels as.
//!
//! One assertion per variant of `LoginsError`, because the codes are a CONTRACT
//! and because `rules/testing.md` requires every typed error variant to appear
//! in at least one test.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::logins::LoginsError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant this area can produce.
fn every_variant() -> Vec<LoginsError> {
    vec![
        LoginsError::AccountMissing,
        LoginsError::SpawnFailed { code: 12 },
        LoginsError::StatusUnreadable,
        LoginsError::AccountBusy,
    ]
}

/// The code `error` is reported as.
fn code_of(error: &LoginsError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn an_account_this_host_cannot_read_is_reported_as_not_found() {
    assert_eq!(
        code_of(&LoginsError::AccountMissing),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn a_password_status_that_cannot_be_read_is_a_fault_and_never_a_success() {
    // "Not locked" and "unreadable" are the same value to a caller that
    // guesses, and the guess would certify a suspension nobody observed.
    assert_eq!(
        code_of(&LoginsError::StatusUnreadable),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn an_account_another_operation_is_holding_is_its_own_code_and_not_a_fault() {
    // This assertion used to read `SystemFailure`, and it was recording a
    // defect rather than a decision: a suspension refused for one second while
    // a nightly backup held the account's lock was reported to the panel as
    // "your server is broken". `common.proto` now carries
    // `ERROR_CODE_ACCOUNT_BUSY` for the one thing it means — this account's
    // wait-free lock was held, nothing ran — so the panel can say "try again
    // in a moment".
    assert_eq!(
        code_of(&LoginsError::AccountBusy),
        ErrorCode::AccountBusy as i32
    );
}

#[test]
fn a_busy_account_and_a_genuine_fault_of_this_host_are_different_codes() {
    // The inverse control for the code above. Without it the busy assertion is
    // satisfied by relabelling the whole area: a password database that cannot
    // be read must STILL read as a fault, because the operator's next step is
    // to go and look at the host rather than to wait.
    assert_ne!(
        code_of(&LoginsError::AccountBusy),
        code_of(&LoginsError::StatusUnreadable),
        "a refusal that changed nothing and a host that cannot answer must not \
         be one code"
    );
    assert_eq!(
        code_of(&LoginsError::StatusUnreadable),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_tool_refusal_is_a_system_failure_carrying_the_status() {
    let error = to_agent_error(&LoginsError::SpawnFailed { code: 12 });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert!(
        error.message.contains("12"),
        "an operator needs the tool's own exit status: {}",
        error.message
    );
}

#[test]
fn no_variant_is_ever_reported_as_the_unspecified_code() {
    for error in every_variant() {
        assert_ne!(
            code_of(&error),
            ErrorCode::Unspecified as i32,
            "{error:?} must be classified"
        );
    }
}

#[test]
fn no_mapped_failure_carries_a_tools_output() {
    // The other half of the claim — that no variant HAS a field an output could
    // be put in — is enforced by the shape of `LoginsError` in the ops crate,
    // where every payload is an i32. That cannot be asserted from here; what can
    // is that this mapping leaves `tool_output` empty and adds no text of its
    // own.
    for error in every_variant() {
        let wire = to_agent_error(&error);
        assert!(
            wire.tool_output.is_empty(),
            "{error:?} must not carry tool output"
        );
        assert_eq!(
            wire.message,
            error.to_string(),
            "the mapping must not invent a message beside the variant's own"
        );
    }
}
