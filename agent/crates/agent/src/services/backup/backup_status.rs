//! The one mapping from backup failures onto the wire error.

use maran_ops::backup::BackupError;

use crate::proto::{AgentError, ErrorCode};

/// Converts a backup or restore failure into the `AgentError` the contract
/// carries.
///
/// It lives beside the service so the match never grows into a handler, and so
/// one variant maps to one code in exactly one place (rules/rust.md "Service
/// anatomy").
///
/// `tool_output` is EMPTY for every variant, and that is a property of
/// [`BackupError`] rather than an omission here: no variant of it carries a
/// program's output or a customer's rows, because the dump client prints table
/// names and occasionally the values it choked on, and a wire field able to
/// hold that would put a customer's data into the operator log. The messages
/// that do carry text carry database names the panel itself supplied, archive
/// member names already escaped, or a remote provider's redacted words.
#[must_use]
pub fn to_agent_error(error: &BackupError) -> AgentError {
    let code = match error {
        // Idempotency outcomes, which the panel branches on: a repeated
        // creation and a repeated delete are successes it must not retry.
        BackupError::AlreadyExists => ErrorCode::AlreadyExists,
        BackupError::NotFound | BackupError::ObjectNotFound => ErrorCode::NotFound,
        // The caller asked for something the agent will not act on: an archive
        // it cannot account for, a database the panel does not know, an
        // endpoint that is not HTTPS.
        BackupError::UnexpectedArchiveMember { .. }
        | BackupError::UnknownDatabase { .. }
        | BackupError::ManifestAccountMismatch
        | BackupError::DestinationInsecure => ErrorCode::InvalidInput,
        // The checks that stand in front of the destructive half of a restore,
        // every one of which leaves the account exactly as it was — which is
        // what rules/proto.md defines VALIDATION_FAILED as: refused, state
        // unchanged.
        BackupError::ChecksumMismatch
        | BackupError::DumpChecksumMismatch { .. }
        | BackupError::ManifestVersionUnknown
        | BackupError::ManifestDisagreesWithSidecar
        | BackupError::DumpTooLarge { .. }
        | BackupError::ArchiveTooLarge { .. }
        | BackupError::UnmintedArtifactName { .. }
        | BackupError::BackupRootUnsafe { .. } => ErrorCode::ValidationFailed,
        // A second operation is running for this account. Not a fault of
        // either: the panel retries, and a stream held open for the length of
        // the first backup is what the refusal exists to avoid.
        BackupError::AlreadyRunning => ErrorCode::AlreadyExists,
        // Everything else is this machine failing at something it was asked to
        // do — including the two rollback variants, which are the most serious
        // answers this area produces and are system failures with a message
        // naming the databases involved.
        _ => ErrorCode::SystemFailure,
    };

    AgentError {
        code: code as i32,
        message: error.to_string(),
        tool_output: String::new(),
    }
}

#[cfg(test)]
#[path = "../../tests/services/backup/backup_status_tests.rs"]
mod tests;
