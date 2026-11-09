//! Turning the outcome of a forced removal into the wire's response.

use crate::proto::{
    AgentError, ForceDeleteEntryOk, ForceDeleteEntryResponse,
    force_delete_entry_response::Result as Outcome,
};

/// Builds the `ForceDeleteEntryResponse` for a finished removal: how many entries went away, or
/// the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn force_deleted_entry(result: Result<u64, AgentError>) -> ForceDeleteEntryResponse {
    let result = match result {
        Ok(entries_removed) => Outcome::Ok(ForceDeleteEntryOk { entries_removed }),
        Err(error) => Outcome::Error(error),
    };

    ForceDeleteEntryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/force_deleted_entry_tests.rs"]
mod tests;
