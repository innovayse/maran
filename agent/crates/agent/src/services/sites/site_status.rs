//! The one mapping from site operation failures onto the wire error.

use maran_ops::sites::SitesOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a site operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// `tool_output` carries a failing program's stderr and nothing else. It is
/// operator-facing by contract and never rendered to a customer, which is why
/// the panel logs it rather than showing it (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &SitesOpError) -> AgentError {
    let (code, tool_output) = match error {
        SitesOpError::AlreadyExists { .. } => (ErrorCode::AlreadyExists, String::new()),
        SitesOpError::NotFound { .. } => (ErrorCode::NotFound, String::new()),
        // The case rules/proto.md defines ERROR_CODE_VALIDATION_FAILED as:
        // "rendered config failed its validator; state rolled back". The
        // stderr is what an administrator is shown and what makes the failure
        // actionable, so it travels in `tool_output`.
        SitesOpError::NginxValidation { stderr } => (ErrorCode::ValidationFailed, stderr.clone()),
        // `sites.proto` states this one: binding a site to a version that is
        // not installed "fails VALIDATION_FAILED".
        SitesOpError::PhpVersionNotInstalled { .. } => (ErrorCode::ValidationFailed, String::new()),
        // A document root that no longer resolves inside the account's home is
        // the caller naming a site the agent will not serve, not a fault of
        // this host — so it is the caller's input that is wrong.
        SitesOpError::UnsafeDocumentRoot { .. } => (ErrorCode::InvalidInput, String::new()),
        // Valid config the service manager refused: a fault of the machine,
        // with the reload's own stderr for the operator.
        SitesOpError::ReloadFailed { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        SitesOpError::Render { .. }
        | SitesOpError::ConfigWrite { .. }
        | SitesOpError::DocumentRoot { .. }
        | SitesOpError::ConfigUnreadable { .. }
        | SitesOpError::LogUnreadable { .. } => (ErrorCode::SystemFailure, String::new()),
        // SitesOpError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps
        // to a system failure: the panel then reports a fault instead of
        // silently treating an unclassified failure as "not found" and
        // carrying on.
        _ => (ErrorCode::SystemFailure, String::new()),
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output,
    }
}

#[cfg(test)]
#[path = "../../tests/services/sites/site_status_tests.rs"]
mod tests;
