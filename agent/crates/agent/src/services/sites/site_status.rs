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
        // A host name of the new site is already served by another vhost, or
        // the site's own vhost appeared while it was being created. The same
        // code as the idempotent answer above, deliberately: both say "a name
        // this site asked for is already on this host", and neither leaves a
        // vhost behind. The refusal at the vhost does leave what the creation
        // made before it — the document root, the log directory and, for a PHP
        // site, the account's pool — none of which serves a request, and a
        // repeated creation converges on them. Only the early idempotent answer
        // writes nothing at all.
        // VALIDATION_FAILED would describe the mechanism (a warning from
        // `nginx -t`, then a rollback) and lose the meaning: the panel reads
        // that code as a rendered configuration the host refused, which is
        // not something a customer fixes by choosing another name. nginx's
        // stderr names the host and travels as tool output for the operator,
        // exactly as a validator's refusal does.
        SitesOpError::ServerNameTaken { stderr, .. } => (ErrorCode::AlreadyExists, stderr.clone()),
        // One request naming one host twice is the caller's input, decided
        // before the host is touched — never a claim another site holds, which
        // is the distinction this variant exists to keep from ALREADY_EXISTS.
        SitesOpError::RepeatedHostName { .. } => (ErrorCode::InvalidInput, String::new()),
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
        // A deadline or an output cap ended the program and its process group was
        // killed. This is a fault of the host, never a verdict on the request: the
        // work may or may not have finished, and `tool_output` stays empty because
        // what the program printed before the kill is neither complete nor safe to
        // forward. Named ahead of the wildcard so a missing arm cannot compile
        // silently (threat note, plan item 11).
        SitesOpError::Bound(_) => (ErrorCode::SystemFailure, String::new()),
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
