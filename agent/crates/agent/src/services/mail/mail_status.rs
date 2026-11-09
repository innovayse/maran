//! The one mapping from mail failures onto the wire error.

use maran_ops::mail::MailError;

use crate::proto::{AgentError, ErrorCode};

/// Maps an error of the mail area onto the contract's typed error.
///
/// One variant to one code, in this one place. `message` is operator-facing
/// English; `tool_output` stays empty, because what a program printed before it
/// failed is neither complete nor safe to forward, and a daemon's refusal already
/// travels in the message of [`MailError::ConfigRejected`], by design, as operator
/// text. No rendered file carries a credential (the hash of a mailbox password and
/// the webmail secret belong to the data plane, which writes them elsewhere), so
/// that text cannot quote one.
#[must_use]
pub fn to_agent_error(error: &MailError) -> AgentError {
    let code = match error {
        // The mail stack never writes into the certificate store, so a missing
        // pair is something the operator can fix by installing one at the path
        // the message names: NOT_FOUND, the same answer the FTPS area gives.
        MailError::CertificateMissing { .. } => ErrorCode::NotFound,
        // The hostname is a valid mail name that the certificate store cannot
        // file material under: the request names something this host cannot
        // use, and repeating it will not help.
        MailError::HostnameNotUsable => ErrorCode::InvalidInput,
        // A daemon's own checker refused the rendered files; the contract's code
        // for exactly this is "state rolled back", and the rollback is what the
        // config-write protocol performed.
        MailError::ConfigRejected { .. } => ErrorCode::ValidationFailed,
        // The request is well formed and this build has no way to carry it out on
        // this release: not retryable against the same agent.
        MailError::UnsupportedRelease => ErrorCode::NotImplemented,
        // A stopped program (deadline or output ceiling) is a fault of the host,
        // never a verdict on the request. Named on its own line so the census of
        // bound-carrying errors can see the arm.
        MailError::Bound(_) => ErrorCode::SystemFailure,
        // Faults of this machine.
        MailError::NotInstalled { .. }
        | MailError::ServiceRefused { .. }
        | MailError::NotListening { .. }
        | MailError::SpawnFailed { .. }
        | MailError::Render
        | MailError::ConfigUnreadable
        | MailError::ConfigWrite { .. }
        | MailError::SocketsUnreadable => ErrorCode::SystemFailure,
        // `MailError` is #[non_exhaustive] (rules/rust.md), so a variant added in
        // the ops crate lands here rather than failing this build. A system
        // failure makes the panel report a fault instead of treating an
        // unclassified failure as "not found" and carrying on.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/mail/mail_status_tests.rs"]
mod tests;
