//! What a finished creation is reported as: the terminal ok, or the typed error.

use maran_ops::backup::{BackupError, BackupSummary};

use crate::proto::{AgentError, CreateBackupOk};
use crate::services::backup::backup_status::to_agent_error;
use crate::services::wire::system_failure::system_failure;

/// Turns the outcome of `ops::backup::create_backup` into the result the
/// terminal message carries.
///
/// Returned as a `Result` so the handler settles its audit record with the very
/// value that becomes the terminal message.
///
/// # Errors
///
/// The wire error of the operation's failure, or a system failure when the
/// summary describes nothing readable.
pub fn created_backup(
    outcome: Result<BackupSummary, BackupError>,
) -> Result<CreateBackupOk, AgentError> {
    let summary = outcome.map_err(|error| to_agent_error(&error))?;

    // `None` is unreachable through `create_backup`, which returns the summary
    // it just wrote and can only write a readable one. Answered rather than
    // asserted because a root daemon does not panic on a reasoning step
    // (rules/rust.md), and answered as a failure rather than as a success with
    // zeroes: a terminal ok carrying an empty digest is a backup the panel
    // would record and no restore could ever verify.
    let details = summary
        .readable_details()
        .ok_or_else(|| system_failure("the created backup describes nothing".to_owned()))?;

    Ok(CreateBackupOk {
        size_bytes: details.artifact_bytes,
        sha256: details.artifact_sha256.clone(),
        database_count: details.manifest.databases.len() as u64,
    })
}
