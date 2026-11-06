//! What a finished restore is reported as: the terminal ok, or the typed error.

use maran_ops::backup::{BackupError, RestoreOutcome};

use crate::proto::{AgentError, RestoreBackupOk};
use crate::services::backup::backup_status::to_agent_error;

/// Turns the outcome of `ops::backup::restore_backup` into the result the
/// terminal message carries.
///
/// Returned as a `Result` so the handler settles its audit record with the very
/// value that becomes the terminal message.
///
/// # Errors
///
/// The wire error of the operation's failure.
pub fn restored_backup(
    outcome: Result<RestoreOutcome, BackupError>,
) -> Result<RestoreBackupOk, AgentError> {
    let restored = outcome.map_err(|error| to_agent_error(&error))?;

    Ok(RestoreBackupOk {
        files_restored: restored.files_restored,
        databases_restored: restored.databases_restored,
        databases_total: restored.databases_total,
        // Always empty here, and read from the outcome rather than invented: a
        // restore that reaches its ok message rolled nothing back. The lists
        // that are not empty ride on `BackupError::RolledBack` and
        // `RolledBackPartially`, which arrive as the error arm and name them in
        // the message.
        not_rolled_back: Vec::new(),
    })
}
