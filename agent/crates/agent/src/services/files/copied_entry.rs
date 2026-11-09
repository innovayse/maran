//! Turning the outcome of a copy into the wire's response.

use maran_ops::files::model::copy_report::CopyReport;

use crate::proto::{
    AgentError, CopyEntryOk, CopyEntryResponse, copy_entry_response::Result as Outcome,
};

/// Builds the `CopyEntryResponse` for a finished copy: the entries and bytes copied, or the
/// typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn copied_entry(result: Result<CopyReport, AgentError>) -> CopyEntryResponse {
    let result = match result {
        Ok(report) => Outcome::Ok(CopyEntryOk {
            entries: report.entries,
            bytes: report.bytes,
        }),
        Err(error) => Outcome::Error(error),
    };

    CopyEntryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/copied_entry_tests.rs"]
mod tests;
