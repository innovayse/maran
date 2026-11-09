//! Turning the outcome of an archive creation into the wire's response.

use maran_ops::files::model::archive_report::ArchiveReport;

use crate::proto::{
    AgentError, CreateArchiveOk, CreateArchiveResponse, create_archive_response::Result as Outcome,
};

/// Builds the `CreateArchiveResponse` for a finished creation: the size of the
/// archive that now exists, or the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn created_archive(result: Result<ArchiveReport, AgentError>) -> CreateArchiveResponse {
    let result = match result {
        Ok(report) => Outcome::Ok(CreateArchiveOk {
            size_bytes: report.bytes,
        }),
        Err(error) => Outcome::Error(error),
    };

    CreateArchiveResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/created_archive_tests.rs"]
mod tests;
