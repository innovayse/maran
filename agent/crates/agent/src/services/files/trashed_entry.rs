//! Turning the outcome of a trash move into the wire's response.

use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::proto::{
    AgentError, TrashEntryOk, TrashEntryResponse, trash_entry_response::Result as Outcome,
};

/// Builds the `TrashEntryResponse` for a finished move: the id the entry can be restored by, or
/// the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn trashed_entry(result: Result<TrashEntryId, AgentError>) -> TrashEntryResponse {
    let result = match result {
        Ok(trash_id) => Outcome::Ok(TrashEntryOk {
            trash_id: trash_id.as_str().to_owned(),
        }),
        Err(error) => Outcome::Error(error),
    };

    TrashEntryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/trashed_entry_tests.rs"]
mod tests;
