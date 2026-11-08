//! Turning the outcome of a trash restore into the wire's response.

use crate::proto::{
    AgentError, RestoreTrashEntryOk, RestoreTrashEntryResponse,
    restore_trash_entry_response::Result as Outcome,
};

/// Builds the `RestoreTrashEntryResponse` for a finished restore: where the entry now is, or
/// the typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn restored_trash_entry(result: Result<String, AgentError>) -> RestoreTrashEntryResponse {
    let result = match result {
        Ok(restored_path) => Outcome::Ok(RestoreTrashEntryOk { restored_path }),
        Err(error) => Outcome::Error(error),
    };

    RestoreTrashEntryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/restored_trash_entry_tests.rs"]
mod tests;
