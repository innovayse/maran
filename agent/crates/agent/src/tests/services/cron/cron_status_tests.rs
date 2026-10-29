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
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
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
        CronError::EntryLimitReached,
        CronError::Account(ManagedAccountError::Reserved),
        CronError::Account(ManagedAccountError::NotFound),
        CronError::Account(ManagedAccountError::SystemIdentity),
        CronError::Account(ManagedAccountError::WrongHome),
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
fn an_allowance_the_account_has_already_used_up_is_its_own_code() {
    // Its own code, and neither of the two it is closest to. Not ALREADY_EXISTS,
    // which the panel answers "you already have that entry" with — the entry is
    // NOT there. Not ACCOUNT_BUSY, whose whole contract is that the caller may
    // reissue the identical request, which here would loop: the answer stays the
    // same until the account holds fewer entries or the allowance is raised.
    assert_eq!(
        code_of(&CronError::EntryLimitReached),
        ErrorCode::LimitReached as i32
    );
    assert_ne!(
        code_of(&CronError::EntryLimitReached),
        ErrorCode::AccountBusy as i32,
        "ERROR_CODE_ACCOUNT_BUSY means only that the lock was held and a caller \
         reading it retries; this refusal is not retryable"
    );
    assert_ne!(
        code_of(&CronError::EntryLimitReached),
        ErrorCode::SystemFailure as i32,
        "a plan limit is not a fault of the host, and reported as one it reaches \
         the customer as an actionless 'something went wrong on your server'"
    );
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

#[test]
fn a_user_that_is_not_a_hosting_account_is_an_invalid_input_and_never_a_fault() {
    // Reserved, a system identity and a home elsewhere are one answer to the
    // panel: the request names an account it does not manage, and no crontab
    // was read or installed. Not SYSTEM_FAILURE, because nothing on the host is
    // broken; not ACCOUNT_BUSY, which invites the identical request again.
    for refusal in [
        ManagedAccountError::Reserved,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ] {
        let code = code_of(&CronError::Account(refusal));

        assert_eq!(code, ErrorCode::InvalidInput as i32);
        assert_ne!(code, ErrorCode::SystemFailure as i32);
        assert_ne!(code, ErrorCode::AccountBusy as i32);
    }
}

#[test]
fn an_account_with_no_password_database_row_is_not_found_and_not_invalid_input() {
    // The inverse control for the arm above: the absent account keeps the
    // answer an absent entry gets, so the catch-all cannot have swallowed it.
    assert_eq!(
        code_of(&CronError::Account(ManagedAccountError::NotFound)),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn the_not_managed_sentence_is_the_same_for_every_refusal_and_names_no_rule() {
    let sentences: Vec<String> = [
        ManagedAccountError::Reserved,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ]
    .into_iter()
    .map(|refusal| to_agent_error(&CronError::Account(refusal)).message)
    .collect();

    assert_eq!(sentences[0], sentences[1]);
    assert_eq!(sentences[1], sentences[2]);
    assert_eq!(sentences[0], "the account is not a hosting account");
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/cron/cron_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> CronError {
    CronError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> CronError {
    CronError::Bound(BoundExceeded::OutputTooLarge {
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
        .position(|line| line.starts_with("CronError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}
