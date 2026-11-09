//! Turning the outcome of emptying the trash into the wire's response.

use crate::proto::{
    AgentError, EmptyTrashOk, EmptyTrashResponse, empty_trash_response::Result as Outcome,
};

/// Builds the `EmptyTrashResponse` for a finished emptying: how many entries went away, or the
/// typed error.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn emptied_trash(result: Result<u64, AgentError>) -> EmptyTrashResponse {
    let result = match result {
        Ok(entries_removed) => Outcome::Ok(EmptyTrashOk { entries_removed }),
        Err(error) => Outcome::Error(error),
    };

    EmptyTrashResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/emptied_trash_tests.rs"]
mod tests;
