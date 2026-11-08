//! A finished export onto its terminal message.

use maran_ops::backup::BackupError;

use crate::proto::{AgentError, ExportBackupDone};
use crate::services::backup::backup_status::to_agent_error;

/// The ok-or-error a finished export is, as the settled result the audit slot
/// takes.
///
/// # Errors
///
/// The wire error of the operation's failure.
pub fn exported_backup(outcome: Result<u64, BackupError>) -> Result<ExportBackupDone, AgentError> {
    outcome
        .map(|artifact_bytes| ExportBackupDone { artifact_bytes })
        .map_err(|error| to_agent_error(&error))
}
