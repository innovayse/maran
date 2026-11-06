//! Turning the outcome of an entry update into the wire's response.

use crate::proto::{
    AgentError, UpdateCronEntryOk, UpdateCronEntryResponse,
    update_cron_entry_response::Result as Outcome,
};

/// Builds the `UpdateCronEntryResponse` for a finished operation: the empty ok payload
/// on success, the typed error otherwise. Failures travel in the payload, not
/// as a gRPC status (rules/proto.md).
#[must_use]
pub fn updated_entry(result: Result<(), AgentError>) -> UpdateCronEntryResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(UpdateCronEntryOk {}),
        Err(error) => Outcome::Error(error),
    };

    UpdateCronEntryResponse {
        result: Some(result),
    }
}
