//! The one mapping from PHP operation failures onto the wire error.

use maran_ops::php::PhpOpError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a PHP operation failure into the `AgentError` the contract carries.
///
/// It lives beside the service rather than inside it so that the match never
/// grows into the handler, and so one variant maps to one code in exactly one
/// place (rules/rust.md "Service anatomy").
///
/// `tool_output` carries a failing program's stderr and nothing else, except
/// that for the PHP install unit the failure carries `systemctl`'s stderr plus
/// the unit name and a `journalctl` hint; the package manager's own output
/// stays in that unit's journal. It is
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
        | PhpOpError::OverridePostSizeBelowUpload { .. }
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
        // A package source outside this host let the install down (issue
        // #251), told apart from a local package-manager failure by
        // `ops::php::classify_install_failure`: a source that answered with an
        // HTTP error, and one nobody could reach. Two codes because the panel
        // answers them differently — a refusal is the mirror's to fix, an
        // unreachable source may clear by itself.
        PhpOpError::PackageSourceRefused { stderr } => {
            (ErrorCode::PackageSourceRefused, stderr.clone())
        }
        PhpOpError::PackageSourceUnreachable { stderr } => {
            (ErrorCode::PackageSourceUnreachable, stderr.clone())
        }
        // The host's package sources do not carry the package (issue #466): the
        // request was sound and the host works, so the panel answers it as a
        // refusal that retrying cannot clear, naming the package in the
        // operator's text.
        PhpOpError::PackageNotAvailable { stderr, .. } => {
            (ErrorCode::PackageNotAvailable, stderr.clone())
        }
        // What the host offers could not be read (issue #466): a fault of this
        // host, with nothing to forward — the reason is an exit status or a
        // line number and already travels in `message`.
        PhpOpError::PackageCatalogueUnavailable { .. } => (ErrorCode::SystemFailure, String::new()),
        PhpOpError::Render { .. } | PhpOpError::ConfigWrite { .. } => {
            (ErrorCode::SystemFailure, String::new())
        }
        // The account's wait-free lock was held by another operation, so the
        // pool file was not rendered, not staged and not validated.
        // ACCOUNT_BUSY rather than SYSTEM_FAILURE: nothing changed and the same
        // request may be reissued in a moment (`common.proto`). No
        // `tool_output`: no tool ran to produce one.
        PhpOpError::AccountBusy { .. } => (ErrorCode::AccountBusy, String::new()),
        // `php.proto`: `ApplyPhpPoolSettings` answers NOT_FOUND when the pool
        // file is absent, so it can never create a pool. No tool ran.
        PhpOpError::PoolMissing { .. } => (ErrorCode::NotFound, String::new()),
        // Extensions (issue #157). A name the family has no package for, and a
        // switch-off the family has no switch for, are things the caller asked
        // for that this host will not do: INVALID_INPUT, no tool ran.
        PhpOpError::ExtensionNotPackaged { .. } | PhpOpError::ExtensionSwitchUnsupported { .. } => {
            (ErrorCode::InvalidInput, String::new())
        }
        // A switch or a module listing that failed carries the stderr an
        // operator acts on; the two "still in the wrong state" verdicts ran no
        // failing tool, the daemon simply did not change.
        PhpOpError::ExtensionSwitchFailed { stderr }
        | PhpOpError::ExtensionProbeFailed { stderr } => (ErrorCode::SystemFailure, stderr.clone()),
        PhpOpError::ExtensionNotLoaded { .. } | PhpOpError::ExtensionStillLoaded { .. } => {
            (ErrorCode::SystemFailure, String::new())
        }
        // A deadline or an output cap ended the program and its process group was
        // killed. This is a fault of the host, never a verdict on the request: the
        // work may or may not have finished, and `tool_output` stays empty because
        // what the program printed before the kill is neither complete nor safe to
        // forward. Named ahead of the wildcard so a missing arm cannot compile
        // silently (threat note, plan item 11).
        PhpOpError::Bound(_) => (ErrorCode::SystemFailure, String::new()),
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
