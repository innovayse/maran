//! Tests for the wire code every database failure travels as.
//!
//! One assertion per variant of `DbError` that this area classifies by name,
//! because the codes are a CONTRACT — `db.proto` names them to callers — and
//! because `rules/testing.md` requires every typed error variant to appear in
//! at least one test. The variants that reach the mapping's wildcard arm carry
//! no named assertion of their own; they are covered by the two loops at the
//! end, over `every_variant`, which is the list this file must keep honest.
//!
//! The last test is about the claim `db_status.rs` makes that nothing it
//! produces can carry the database client's output. It pins the half of that
//! claim this file can see — the mapping invents no text and fills no
//! `tool_output` — and says in the test which half it cannot
//! (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::db::DbError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant this area can produce.
///
/// Hand-written, and it cannot be made unable to fall behind from this crate.
/// It already did: `StatementRefused`, `ClientUnavailable` and `ClientKilled`
/// were split out of `ClientFailed` in the ops crate and were missing here, so
/// both loops below silently covered five variants while claiming to cover
/// every one — a decay with no failing test to announce it.
///
/// The usual guard, a `match` with no wildcard arm so that a new variant is a
/// compile error, is not available here. [`DbError`] is `#[non_exhaustive]` and
/// is defined in another crate, and rustc REFUSES an exhaustive match on it
/// from outside that crate: `error[E0004]: non-exhaustive patterns: `&_` not
/// covered … `DbError` is marked as non-exhaustive, so a wildcard `_` is
/// necessary to match exhaustively` (measured on a two-crate reduction of this
/// exact shape). The required wildcard arm is reached only at run time, and
/// nothing in this crate can construct a variant it does not know about, so no
/// test can reach it either. A guard would have to live beside the enum in
/// `ops`, where a match is exhaustive by default — which is the same reason
/// `to_agent_error` carries a `_` arm.
fn every_variant() -> Vec<DbError> {
    vec![
        DbError::AlreadyExists,
        DbError::NotFound,
        DbError::ClientFailed { code: 1064 },
        // The three that were missing. They are the failures where no server
        // answered — a statement this host refused to send, a client that could
        // not be started, and a client the machine killed — and they reach
        // `to_agent_error` through its wildcard arm, which is precisely the arm
        // no reader can check by eye.
        DbError::StatementRefused,
        DbError::ClientUnavailable,
        DbError::ClientKilled { signal: 9 },
        DbError::Unparsable,
        DbError::AccessDenied,
    ]
}

/// The code `error` is reported as.
fn code_of(error: &DbError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_database_that_is_already_there_is_an_idempotency_outcome_not_a_fault() {
    assert_eq!(
        code_of(&DbError::AlreadyExists),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_database_that_is_not_there_is_reported_as_not_found() {
    assert_eq!(code_of(&DbError::NotFound), ErrorCode::NotFound as i32);
}

#[test]
fn a_server_that_refuses_the_agents_own_connection_is_a_fault_of_the_host() {
    // Not INVALID_INPUT. Nothing the panel sent can cause it — the agent
    // connects over the socket with no credential at all — so telling a
    // customer their input was wrong would send them to change something that
    // is already correct. It means socket authentication is not enabled for
    // root@localhost, which is an operator's job.
    assert_eq!(
        code_of(&DbError::AccessDenied),
        ErrorCode::SystemFailure as i32
    );
}

#[test]
fn a_refusal_the_area_does_not_name_is_a_system_failure_carrying_the_number() {
    let error = to_agent_error(&DbError::ClientFailed { code: 1064 });

    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert!(
        error.message.contains("1064"),
        "an operator needs the server's own error number: {}",
        error.message
    );
}

#[test]
fn output_the_agent_could_not_read_is_a_system_failure_rather_than_a_guess() {
    assert_eq!(
        code_of(&DbError::Unparsable),
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
fn no_mapped_failure_carries_the_database_clients_output() {
    // The other half of the claim — that no variant HAS a field an output could
    // be put in — is enforced by the shape of `DbError` in the ops crate, where
    // every payload is an i32. That cannot be asserted from here; what can is
    // that this mapping leaves `tool_output` empty and adds no text of its own.
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
