//! Turning the outcome of a directory creation into the wire's response.

use crate::proto::{
    AgentError, CreateDirectoryOk, CreateDirectoryResponse,
    create_directory_response::Result as Outcome,
};

/// Builds the `CreateDirectoryResponse` for a finished creation: the empty ok payload on
/// success, the typed error otherwise.
///
/// Failures travel in the payload, not as a gRPC status (rules/proto.md).
#[must_use]
pub fn created_directory(result: Result<(), AgentError>) -> CreateDirectoryResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(CreateDirectoryOk {}),
        Err(error) => Outcome::Error(error),
    };

    CreateDirectoryResponse {
        result: Some(result),
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/created_directory_tests.rs"]
mod tests;
