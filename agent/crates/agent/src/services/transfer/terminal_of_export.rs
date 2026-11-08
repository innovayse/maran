//! A finished export's result wrapped into the arm of the response that carries it.

use crate::proto::{AgentError, ExportBackupDone, export_backup_response};

/// Wraps the settled result of an export into the terminal message of its stream.
#[must_use]
pub fn terminal_of_export(
    result: Result<ExportBackupDone, AgentError>,
) -> export_backup_response::Result {
    match result {
        Ok(done) => export_backup_response::Result::Done(done),
        Err(error) => export_backup_response::Result::Error(error),
    }
}
