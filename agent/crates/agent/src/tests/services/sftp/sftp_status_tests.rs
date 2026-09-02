//! Tests for the wire code every SFTP login failure travels as.
//!
//! One assertion per variant of `SftpError`, because the codes are a CONTRACT —
//! `ftp.proto` names them to callers — and because `rules/testing.md` requires
//! every typed error variant to appear in at least one test.
//!
//! The last test is about the claim `sftp_status.rs` makes that nothing it
//! produces can carry a tool's output. It pins the half of that claim this file
//! can see — the mapping invents no text and fills no `tool_output` — and says
//! in the test which half it cannot (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::sftp::SftpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant this area can produce.
fn every_variant() -> Vec<SftpError> {
    vec![
        SftpError::AlreadyExists,
        SftpError::NotFound,
        SftpError::AccountMissing,
        SftpError::SpawnFailed { code: 12 },
        SftpError::PasswordRejected,
        SftpError::JailFailed,
    ]
}

/// The code `error` is reported as.
fn code_of(error: &SftpError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_login_that_is_already_there_is_an_idempotency_outcome_not_a_fault() {
    assert_eq!(
        code_of(&SftpError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_login_that_is_not_there_is_reported_as_not_found() {
    assert_eq!(code_of(&SftpError::NotFound), ErrorCode::NotFound as i32);
}

#[test]
fn an_account_this_host_never_created_is_reported_as_not_found_too() {
    // The same answer to the panel — something it asked about is not here — but
    // a different variant in `ops`, because the two send an operator to
    // different places: one means the login is gone, the other that the account
    // was never made.
    assert_eq!(
        code_of(&SftpError::AccountMissing),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn a_password_the_host_refuses_is_a_validation_failure_not_invalid_input() {
    // The agent's own alphabet check passed, so this is the HOST's opinion —
    // usually its PAM complexity policy — of a value the contract accepts.
    // Reporting it as INVALID_INPUT would tell the panel the agent refused the
    // password, and it did not.
    assert_eq!(
        code_of(&SftpError::PasswordRejected),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_jail_that_did_not_take_effect_is_a_fault_and_never_a_success() {
    // A login created against a jail that is not mounted works, and finds an
    // empty directory where the customer's files should be. To a customer that
    // reads as data loss, so the panel must see a fault.
    assert_eq!(
        code_of(&SftpError::JailFailed),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_tool_refusal_the_area_does_not_name_is_a_system_failure_carrying_the_status() {
    let error = to_agent_error(&SftpError::SpawnFailed { code: 12 });

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
    // be put in — is enforced by the shape of `SftpError` in the ops crate,
    // where every payload is an i32. That cannot be asserted from here; what can
    // is that this mapping leaves `tool_output` empty and adds no text of its
    // own. It matters because the output a `chpasswd` refusal would carry is the
    // customer's password.
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
