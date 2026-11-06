//! The terminal message of a creation stream.

use crate::proto::{AgentError, CreateBackupOk, create_backup_response};

/// Wraps the result a creation settled its audit record with into the arm of the
/// terminal message that carries it.
#[must_use]
pub fn terminal_of_creation(
    result: Result<CreateBackupOk, AgentError>,
) -> create_backup_response::Result {
    match result {
        Ok(ok) => create_backup_response::Result::Ok(ok),
        Err(error) => create_backup_response::Result::Error(error),
    }
}
