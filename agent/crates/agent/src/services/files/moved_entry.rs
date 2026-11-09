//! Turning the outcome of a move into the wire's response.

use crate::proto::{
    AgentError, MoveEntryOk, MoveEntryResponse, move_entry_response::Result as Outcome,
};

/// Builds the `MoveEntryResponse` for a finished move: the empty ok payload on success, the
/// typed error otherwise.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn moved_entry(result: Result<(), AgentError>) -> MoveEntryResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(MoveEntryOk {}),
        Err(error) => Outcome::Error(error),
    };

    MoveEntryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/moved_entry_tests.rs"]
mod tests;
