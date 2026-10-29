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
        SftpError::AccountBusy,
        SftpError::AccountNotManaged,
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

#[test]
fn a_login_operation_refused_because_the_account_is_busy_is_its_own_code() {
    // It used to fall through the mapping's `_` arm onto SYSTEM_FAILURE. The
    // refusal happens before `useradd`, `chpasswd` or the jail: nothing ran.
    assert_eq!(
        code_of(&SftpError::AccountBusy),
        ErrorCode::AccountBusy as i32
    );
}

#[test]
fn a_busy_account_and_a_jail_that_did_not_take_effect_are_different_codes() {
    // The inverse control. A jail that did not take effect reads to a customer
    // as data loss and must still be a fault, not something to wait out.
    assert_eq!(
        code_of(&SftpError::JailFailed),
        ErrorCode::SystemFailure as i32
    );
    assert_ne!(
        code_of(&SftpError::AccountBusy),
        code_of(&SftpError::JailFailed)
    );
}

#[test]
fn a_user_that_is_not_a_hosting_account_is_an_invalid_input_and_never_a_fault() {
    // The managed-account proof refused the name before any password was piped
    // or any login created. A request fault: the user may well exist (so not
    // NOT_FOUND) and nothing on the host is broken (so not SYSTEM_FAILURE).
    let refusal = code_of(&SftpError::AccountNotManaged);

    assert_eq!(refusal, ErrorCode::InvalidInput as i32);
    for wrong in [
        ErrorCode::SystemFailure,
        ErrorCode::NotFound,
        ErrorCode::AccountBusy,
        ErrorCode::AlreadyExists,
    ] {
        assert_ne!(refusal, wrong as i32);
    }
}

#[test]
fn an_account_that_is_not_there_and_one_that_is_not_managed_are_different_answers() {
    assert_eq!(
        code_of(&SftpError::AccountMissing),
        ErrorCode::NotFound as i32
    );
    assert_ne!(
        code_of(&SftpError::AccountMissing),
        code_of(&SftpError::AccountNotManaged)
    );
}

#[test]
fn the_not_managed_sentence_is_fixed_and_names_no_rule() {
    assert_eq!(
        to_agent_error(&SftpError::AccountNotManaged).message,
        "the account is not a hosting account"
    );
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/sftp/sftp_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> SftpError {
    SftpError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> SftpError {
    SftpError::Bound(BoundExceeded::OutputTooLarge {
        program: "/usr/sbin/stand-in".to_owned(),
        stream: OutputStream::Stdout,
        cap: 4_194_304,
    })
}

/// A deadline is a fault of the host: a system failure, no tool text, and a
/// message that names the program that was killed.
#[test]
fn a_timeout_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&timed_out());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// An output cap is the same kind of fault as a deadline.
#[test]
fn an_output_overflow_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&output_too_large());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// The wildcard arm answers `SystemFailure` too, so deleting the explicit arm
/// changes no behaviour and the two tests above cannot notice it. This one reads
/// the source: the bound must be named, and named before the wildcard.
#[test]
fn the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard() {
    let code_lines: Vec<&str> = MAPPING_SOURCE
        .lines()
        .map(str::trim_start)
        .filter(|line| !line.starts_with("//"))
        .collect();
    let wildcard = code_lines.iter().position(|line| line.starts_with("_ =>"));
    let bound = code_lines
        .iter()
        .position(|line| line.starts_with("SftpError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}
