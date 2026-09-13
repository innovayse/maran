//! The one mapping from database operation failures onto the wire error.

use maran_ops::db::DbError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a database operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant, and that is structural rather
/// than a choice made here.** No [`DbError`] variant has a field that could hold
/// the client's output: every payload is an `i32`. The realistic leak in this
/// area is the server quoting back what it refused — `Access denied for user
/// 'alice_shop'@'localhost'`, and on some paths the whole statement, which for
/// `CREATE USER … IDENTIFIED BY '…'` is the customer's password in full. There
/// is nothing here to copy that into, so this mapping cannot reintroduce it and
/// a future variant carrying a string would have to be added deliberately in
/// `ops` first (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &DbError) -> AgentError {
    let code = match error {
        // `db.proto`: a repeated create "returns AlreadyExists without changing
        // the existing password". An idempotency outcome, not a fault.
        DbError::AlreadyExists => ErrorCode::AlreadyExists,
        // `db.proto`: "dropping a non-existent database returns NotFound", and
        // measuring one that is not there is the same shape of answer.
        DbError::NotFound => ErrorCode::NotFound,
        // Faults of this machine, not of the request. AccessDenied is the
        // narrowest of them: the server refused the AGENT's own connection,
        // which means socket authentication is not enabled for root@localhost —
        // a condition the installer verifies and an operator must be pointed at.
        // It is deliberately not INVALID_INPUT: nothing the panel sent could
        // have caused it, and telling a customer their input was wrong would
        // send them to change something that is already correct.
        DbError::AccessDenied | DbError::ClientFailed { .. } | DbError::Unparsable => {
            ErrorCode::SystemFailure
        }
        // DbError is #[non_exhaustive] (rules/rust.md), so a variant added in
        // the ops crate lands here rather than failing this build. It maps to a
        // system failure: the panel then reports a fault instead of silently
        // treating an unclassified failure as "not found" and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/db/db_status_tests.rs"]
mod tests;
