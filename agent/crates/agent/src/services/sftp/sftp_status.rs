//! The one mapping from SFTP login failures onto the wire error.

use maran_ops::sftp::SftpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts an SFTP operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant, and that is structural rather
/// than a choice made here.** No [`SftpError`] variant has a field that could
/// hold a tool's output: every payload is an `i32`. The realistic leak in this
/// area is `chpasswd` or PAM quoting back the line it refused, which contains
/// the customer's password in full — there is nothing here to copy that into
/// (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &SftpError) -> AgentError {
    let code = match error {
        // `ftp.proto`: a repeated create "returns ALREADY_EXISTS and its
        // password is NOT changed". An idempotency outcome, not a fault.
        SftpError::AlreadyExists => ErrorCode::AlreadyExists,
        // `ftp.proto`: "deleting a login that is not there returns NOT_FOUND".
        // The hosting account being absent is the same answer to the panel —
        // something it asked about is not on this host — but a DIFFERENT
        // variant in `ops`, because it sends an operator somewhere else: one
        // means the login is gone, the other that the account was never made.
        SftpError::NotFound | SftpError::AccountMissing => ErrorCode::NotFound,
        // The one failure that leaves a login the customer cannot use: the
        // account exists and the password it was to be reached with was not
        // set. VALIDATION_FAILED rather than SYSTEM_FAILURE because that is
        // what `chpasswd` refusing a line means — the password itself was not
        // accepted, most often by the host's own PAM complexity policy, which
        // is a thing the panel can tell a customer to change. It is not
        // INVALID_INPUT: the agent's own alphabet check passed, so this is the
        // HOST's opinion of a value the contract accepted.
        SftpError::PasswordRejected => ErrorCode::ValidationFailed,
        // Faults of this machine. A jail that did not take effect is one of
        // them and not an input problem: the login would work and find an empty
        // directory where the customer's files should be, which reads to a
        // customer as data loss, so the panel must report a fault rather than a
        // success.
        SftpError::JailFailed | SftpError::SpawnFailed { .. } => ErrorCode::SystemFailure,
        // SftpError is #[non_exhaustive] (rules/rust.md), so a variant added in
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
#[path = "../../tests/services/sftp/sftp_status_tests.rs"]
mod tests;
