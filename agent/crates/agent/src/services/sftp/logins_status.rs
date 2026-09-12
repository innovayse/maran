//! The one mapping from file-transfer login failures onto the wire error.

use maran_ops::logins::LoginsError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a login enumeration or lock failure into the `AgentError` the
/// contract carries.
///
/// It lives in this folder because the `SetAccountLoginsLocked` rpc is served
/// here, beside the SFTP rpcs it was written with — `ops::logins` is an
/// operation area and not a proto service, so it has no folder of its own to
/// map from. The mapping is its own file for the reason `sftp_status.rs` is:
/// the match never grows into the handler, and one variant maps to one code in
/// exactly one place (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant, and that is structural rather
/// than a choice made here.** No [`LoginsError`] variant has a field that could
/// hold a tool's output: every payload is an `i32` (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &LoginsError) -> AgentError {
    let code = match error {
        // The account is not on this host, or its password database cannot be
        // read. Something the panel asked about is not there to answer for.
        LoginsError::AccountMissing => ErrorCode::NotFound,
        // Faults of this machine. A status that could not be read is one of
        // them and is deliberately not a success: "not locked" and "unreadable"
        // are the same value to a caller that guesses, and the guess would
        // certify a suspension nobody observed.
        LoginsError::SpawnFailed { .. } | LoginsError::StatusUnreadable => ErrorCode::SystemFailure,
        // The account's wait-free lock was held by another operation, so no
        // login was enumerated and none was locked or unlocked. ACCOUNT_BUSY
        // rather than SYSTEM_FAILURE, which is where it used to land and which
        // made a suspension refused for one second indistinguishable from a
        // host whose password database cannot be read. The distinction matters
        // most here: a suspension the panel must retry, and a status it must
        // never guess at, were the same code.
        LoginsError::AccountBusy => ErrorCode::AccountBusy,
        // LoginsError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps
        // to a system failure: the panel then reports a fault instead of
        // silently treating an unclassified failure as "not found".
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/sftp/logins_status_tests.rs"]
mod tests;
