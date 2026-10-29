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
        LoginsError::AccountNotManaged,
        LoginsError::SessionCullFailed { code: 1 },
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

#[test]
fn a_user_that_is_not_a_hosting_account_is_an_invalid_input_and_never_a_fault() {
    // The managed-account proof refused the name, so nothing was locked, unlocked
    // or killed. A request fault with a fixed sentence: not NOT_FOUND (the user
    // may exist), not SYSTEM_FAILURE (nothing on this host is broken), and not
    // ACCOUNT_BUSY, which tells a caller to reissue the identical request.
    let refusal = code_of(&LoginsError::AccountNotManaged);

    assert_eq!(refusal, ErrorCode::InvalidInput as i32);
    for wrong in [
        ErrorCode::SystemFailure,
        ErrorCode::NotFound,
        ErrorCode::AccountBusy,
    ] {
        assert_ne!(refusal, wrong as i32);
    }
}

#[test]
fn the_not_managed_sentence_is_fixed_and_names_no_rule() {
    let message = to_agent_error(&LoginsError::AccountNotManaged).message;

    assert_eq!(message, "the account is not a hosting account");
}

#[test]
fn a_session_cull_that_failed_is_a_system_failure_carrying_the_status() {
    let wire = to_agent_error(&LoginsError::SessionCullFailed { code: 3 });

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.message.contains('3'), "{}", wire.message);
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/sftp/logins_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> LoginsError {
    LoginsError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> LoginsError {
    LoginsError::Bound(BoundExceeded::OutputTooLarge {
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
        .position(|line| line.starts_with("LoginsError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}
