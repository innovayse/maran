//! Turning the outcome of an extraction into the wire's response.

use maran_ops::files::model::archive_report::ArchiveReport;

use crate::proto::{
    AgentError, ExtractArchiveOk, ExtractArchiveResponse,
    extract_archive_response::Result as Outcome,
};

/// Builds the `ExtractArchiveResponse` for a finished extraction: how many members
/// were written, or the typed error.
///
/// The wire field is 32 bits and the agent's own ceiling on members is a million,
/// so the conversion cannot fail; a count that somehow did not fit is reported as
/// the largest the field holds rather than as zero, which would read as "nothing
/// was extracted".
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn extracted_archive(result: Result<ArchiveReport, AgentError>) -> ExtractArchiveResponse {
    let result = match result {
        Ok(report) => Outcome::Ok(ExtractArchiveOk {
            entry_count: u32::try_from(report.entries).unwrap_or(u32::MAX),
        }),
        Err(error) => Outcome::Error(error),
    };

    ExtractArchiveResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/extracted_archive_tests.rs"]
mod tests;
