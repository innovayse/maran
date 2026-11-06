//! The terminal message of a restore stream.

use crate::proto::{AgentError, RestoreBackupOk, restore_backup_response};

/// Wraps the result a restore settled its audit record with into the arm of the
/// terminal message that carries it.
#[must_use]
pub fn terminal_of_restoration(
    result: Result<RestoreBackupOk, AgentError>,
) -> restore_backup_response::Result {
    match result {
        Ok(ok) => restore_backup_response::Result::Ok(ok),
        Err(error) => restore_backup_response::Result::Error(error),
    }
}
