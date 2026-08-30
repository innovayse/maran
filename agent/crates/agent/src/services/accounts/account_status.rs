//! The one mapping from account operation failures onto the wire error.

use maran_ops::accounts::AccountError;

use crate::proto::{AgentError, ErrorCode};

/// Converts an operation failure into the `AgentError` the contract carries.
///
/// It lives beside the service rather than inside it so that the match never grows
/// into the handler, and so one variant maps to one code in exactly one place
/// (rules/rust.md "Service anatomy").
///
/// `tool_output` carries a failing program's stderr and nothing else. It is
/// operator-facing by contract and never rendered to a customer, which is why the
/// panel logs it rather than showing it (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &AccountError) -> AgentError {
    let (code, tool_output) = match error {
        AccountError::InvalidName(_) => (ErrorCode::InvalidInput, String::new()),
        AccountError::AlreadyExists { .. } => (ErrorCode::AlreadyExists, String::new()),
        AccountError::NotFound { .. } => (ErrorCode::NotFound, String::new()),
        AccountError::CommandFailed { stderr, .. } => (ErrorCode::SystemFailure, stderr.clone()),
        AccountError::CommandUnavailable { .. } | AccountError::UnreadableOutput { .. } => {
            (ErrorCode::SystemFailure, String::new())
        }
        // AccountError is #[non_exhaustive] (rules/rust.md), so a variant added in the
        // ops crate lands here rather than failing this build. It maps to a system
        // failure: the panel then reports a fault instead of silently treating an
        // unclassified failure as "not found" and carrying on.
        _ => (ErrorCode::SystemFailure, String::new()),
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output,
    }
}
