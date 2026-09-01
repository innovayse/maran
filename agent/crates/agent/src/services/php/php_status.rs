//! The one mapping from PHP operation failures onto the wire error.

use maran_ops::php::PhpOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a PHP operation failure into the `AgentError` the contract carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// `tool_output` carries a failing program's stderr and nothing else. It is
/// operator-facing by contract and never rendered to a customer, which is why
/// the panel logs it rather than showing it (rules/security.md item 8).
#[must_use]
pub fn to_agent_error(error: &PhpOpError) -> AgentError {
    let (code, tool_output) = match error {
        // `php.proto`: a version outside the supported set is INVALID_INPUT
        // "rather than a package manager error". The overrides are the same
        // shape of answer — a name off the whitelist, a malformed value, one
        // out of range, or one carrying a control character are all the caller
        // asking for something the agent will not write.
        PhpOpError::UnsupportedVersion { .. }
        | PhpOpError::OverrideNotAllowed { .. }
        | PhpOpError::OverrideMalformed { .. }
        | PhpOpError::OverrideOutOfRange { .. }
        | PhpOpError::OverrideControlCharacter { .. }
        | PhpOpError::WorkerBudgetOutOfRange { .. } => (ErrorCode::InvalidInput, String::new()),
        // `sites.proto`: binding to a version that is not installed "fails
        // VALIDATION_FAILED".
        PhpOpError::PhpVersionNotInstalled { .. } => (ErrorCode::ValidationFailed, String::new()),
        // The case rules/proto.md defines ERROR_CODE_VALIDATION_FAILED as:
        // "rendered config failed its validator; state rolled back" — here the
        // validator is `php-fpm -t` and the previous pool is back in place.
        PhpOpError::PoolValidation { stderr } => (ErrorCode::ValidationFailed, stderr.clone()),
        // Faults of this machine, each with the stderr an operator acts on: a
        // repository to fix, a unit to look at, or a service that would not
        // reload.
        PhpOpError::PackageManager { stderr }
        | PhpOpError::ServiceEnable { stderr }
        | PhpOpError::ReloadFailed { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        PhpOpError::Render { .. } | PhpOpError::ConfigWrite { .. } => {
            (ErrorCode::SystemFailure, String::new())
        }
        // PhpOpError is #[non_exhaustive] (rules/rust.md), so a variant added
        // in the ops crate lands here rather than failing this build. It maps
        // to a system failure: the panel then reports a fault instead of
        // silently treating an unclassified failure as "not installed" and
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
#[path = "../../tests/services/php/php_status_tests.rs"]
mod tests;
