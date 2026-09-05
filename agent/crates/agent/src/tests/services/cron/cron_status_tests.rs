//! Tests for the wire code every cron failure travels as.
//!
//! One assertion per variant of `CronError`, because the codes are a CONTRACT —
//! `cron.proto` names them to callers — and because `rules/testing.md` requires
//! every typed error variant to appear in at least one test.
//!
//! The last two are about the claim `cron_status.rs` makes that nothing it
//! produces can carry `crontab(1)`'s output. This file pins the half it can
//! see — the mapping invents no text and fills no `tool_output` — and says in
//! the test which half it cannot (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;
use maran_ops::cron::CronError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant this area can produce.
fn every_variant() -> Vec<CronError> {
    vec![
        CronError::AlreadyExists,
        CronError::NotFound,
        CronError::Privilege(PrivError::WorkFailed),
        CronError::CrontabRefused { code: 1 },
        CronError::EntryFileUnwritable,
        CronError::EntryFileUnreadable,
        CronError::EntryFileUnremovable,
        CronError::EntryIdUnavailable,
    ]
}

/// The code `error` is reported as.
fn code_of(error: &CronError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn an_entry_that_is_already_there_is_an_idempotency_outcome_not_a_fault() {
    assert_eq!(
        code_of(&CronError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn an_entry_this_account_does_not_own_is_reported_as_not_found() {
    assert_eq!(code_of(&CronError::NotFound), ErrorCode::NotFound as i32);
}

#[test]
fn a_table_the_program_refused_is_a_fault_of_the_host_carrying_its_status() {
    let error = to_agent_error(&CronError::CrontabRefused { code: 42 });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert!(
        error.message.contains("42"),
        "the program's exit status is all an operator gets, so it must travel: {}",
        error.message
    );
}

#[test]
fn a_failed_privilege_drop_is_a_fault_of_the_host_rather_than_of_the_request() {
    // Not INVALID_INPUT: the account name was validated before this could be
    // reached, so a drop that failed means the host cannot resolve an account
    // it created — an operator's problem, not a customer's.
    assert_eq!(
        code_of(&CronError::Privilege(PrivError::WorkFailed)),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn the_three_entry_file_failures_are_faults_of_the_host() {
    for error in [
        CronError::EntryFileUnwritable,
        CronError::EntryFileUnreadable,
        CronError::EntryFileUnremovable,
    ] {
        assert_eq!(
            code_of(&error),
            ErrorCode::SystemFailure as i32,
            "{error:?}"
        );
    }
}

#[test]
fn a_host_that_cannot_mint_an_id_is_a_system_failure_and_nothing_was_written() {
    assert_eq!(
        code_of(&CronError::EntryIdUnavailable),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn no_variant_is_ever_reported_as_the_unspecified_code() {
    // ERROR_CODE_UNSPECIFIED means "the agent failed without classifying the
    // failure" (common.proto). A variant that fell through to it would be a
    // panel that cannot tell an idempotency outcome from a broken host.
    for error in every_variant() {
        assert_ne!(
            code_of(&error),
            ErrorCode::Unspecified as i32,
            "{error:?} must be classified"
        );
    }
}

#[test]
fn no_mapped_failure_carries_a_programs_output() {
    // The other half of the claim — that no variant HAS a field an output could
    // be put in — is enforced by the shape of `CronError` in the ops crate,
    // where every payload is an i32. That cannot be asserted from here; what can
    // is that this mapping leaves `tool_output` empty and adds no text of its
    // own. It matters here more than in most areas: a managed crontab carries
    // the account's own environment assignments, so a quoted-back table is a
    // secret in the operator log.
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
