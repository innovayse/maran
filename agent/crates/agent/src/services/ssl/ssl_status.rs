//! The one mapping from certificate operation failures onto the wire error.

use maran_ops::ssl::SslOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a certificate operation failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// **Nothing here can carry private key material.** That is a property of
/// [`SslOpError`], which has no variant that holds it — the mapping merely
/// stays inside that guarantee by copying only the fields the enum defines and
/// inventing no message of its own.
#[must_use]
pub fn to_agent_error(error: &SslOpError) -> AgentError {
    let (code, tool_output) = match error {
        // The caller sent a pair that does not belong together, or a half
        // openssl cannot read. Nothing was written, and no retry of the same
        // bytes will do better: it is the input that is wrong.
        SslOpError::KeyDoesNotMatchCertificate
        | SslOpError::MalformedCertificate { .. }
        | SslOpError::MalformedPrivateKey => (ErrorCode::InvalidInput, String::new()),
        // `ssl.proto`: removing when no certificate is installed returns
        // NotFound, and a certificate for a site that is not configured is the
        // same shape of answer.
        SslOpError::NotFound { .. } | SslOpError::SiteNotFound { .. } => {
            (ErrorCode::NotFound, String::new())
        }
        // A real certificate is in place and this call would have replaced it.
        SslOpError::AlreadyExists { .. } => (ErrorCode::AlreadyExists, String::new()),
        // The case rules/proto.md defines ERROR_CODE_VALIDATION_FAILED as:
        // "rendered config failed its validator; state rolled back". The site
        // is back on its previous vhost, and the stderr is what makes the
        // refusal actionable for an administrator.
        SslOpError::NginxValidation { stderr } => (ErrorCode::ValidationFailed, stderr.clone()),
        // Valid config the service manager refused, and everything else that
        // is a fault of this machine rather than of the request.
        SslOpError::ReloadFailed { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        SslOpError::ExpiryUnreadable { .. }
        | SslOpError::ToolUnavailable { .. }
        | SslOpError::MaterialWrite { .. }
        | SslOpError::Render { .. }
        | SslOpError::ConfigWrite { .. } => (ErrorCode::SystemFailure, String::new()),
        // SslOpError is #[non_exhaustive] (rules/rust.md), so a variant added
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
#[path = "../../tests/services/ssl/ssl_status_tests.rs"]
mod tests;
