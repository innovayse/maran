//! The one mapping from FTPS failures onto the wire error.

use maran_ops::ftps::FtpsError;

use crate::proto::{AgentError, ErrorCode};

/// Converts an FTPS operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **`tool_output` is empty for every variant, and it is a decision rather than
/// an omission.** Two variants here DO carry text a tool printed —
/// [`FtpsError::ConfigRejected`] and [`FtpsError::ServiceRefused`] — and the
/// contract has a field for exactly that. It is left empty because the realistic
/// leak in this area is a daemon or PAM quoting back the line it refused, and
/// the line `chpasswd` is handed contains a customer's password in full
/// (rules/security.md item 8). The operator sees that output in the agent's own
/// log, where `Display` puts it; it does not travel to a caller that might show
/// it. A future task that wants it on the wire has to say which variants are
/// safe, one at a time.
#[must_use]
pub fn to_agent_error(error: &FtpsError) -> AgentError {
    let code = match error {
        // `ftp.proto`: a repeated create "returns ALREADY_EXISTS and its
        // password is NOT changed". An idempotency outcome, not a fault.
        FtpsError::AlreadyExists => ErrorCode::AlreadyExists,
        // Three ways for something the caller named not to be here, and they are
        // one answer to the panel — the thing you asked about is not on this
        // host — while staying distinct in `ops`, because each sends an operator
        // somewhere else. A missing certificate is NOT_FOUND rather than a
        // system failure for the same reason: the operator's next step is to
        // install material at the path the message names, which is an action
        // they can take.
        FtpsError::NotFound | FtpsError::AccountMissing | FtpsError::CertificateMissing { .. } => {
            ErrorCode::NotFound
        }
        // The rendered configuration was refused by the daemon that would have
        // to read it. VALIDATION_FAILED is the code the contract defines for
        // exactly this — "rendered config failed its validator; state rolled
        // back" — and the rollback is what `enable_ftps` performs.
        FtpsError::ConfigRejected { .. } => ErrorCode::ValidationFailed,
        // The host's opinion of a password the agent's own alphabet check
        // accepted, most often its PAM complexity policy. Not INVALID_INPUT: the
        // contract's own check passed, so this is not the caller's mistake to
        // have avoided, and it is a thing the panel can tell a customer to
        // change.
        FtpsError::PasswordRejected => ErrorCode::ValidationFailed,
        // Faults of this machine. A jail that did not take effect is one of them
        // and not an input problem: the login would work and find an empty
        // directory where the customer's files should be, which reads to a
        // customer as data loss.
        //
        // `SuspensionNotRestored` is here too, and it is the sharpest thing this
        // service can report: the password IS set and a suspended login is OPEN.
        // A system failure is the only code that makes the panel show a fault
        // rather than a success, which is the behaviour that condition needs.
        FtpsError::JailFailed
        | FtpsError::SpawnFailed { .. }
        | FtpsError::ServiceRefused { .. }
        | FtpsError::NotListening
        | FtpsError::Render
        | FtpsError::ConfigUnreadable
        | FtpsError::ConfigWrite { .. }
        | FtpsError::StatusUnreadable
        | FtpsError::SuspensionNotRestored
        | FtpsError::AccountIdentityChanged => ErrorCode::SystemFailure,
        // The hosting account's wait-free lock was held by another operation,
        // so this one was refused before vsftpd's user database, the jail or
        // `chpasswd` was touched. ACCOUNT_BUSY rather than SYSTEM_FAILURE,
        // which is where it used to land: nothing on this host is broken and
        // nothing changed, so the panel says "try again in a moment" and may
        // reissue the identical request (`common.proto`).
        //
        // It is deliberately NOT grouped with `AccountIdentityChanged` above,
        // which is the opposite answer: that one means the account was remade
        // under the operation, so the request must NOT be replayed blindly.
        FtpsError::AccountBusy => ErrorCode::AccountBusy,
        // FtpsError is #[non_exhaustive] (rules/rust.md), so a variant added in
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
#[path = "../../tests/services/ftps/ftps_status_tests.rs"]
mod tests;
