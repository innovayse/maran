//! Turning the outcome of an entry deletion into the wire's response.

use crate::proto::{
    AgentError, DeleteCronEntryOk, DeleteCronEntryResponse,
    delete_cron_entry_response::Result as Outcome,
};

/// Builds the `DeleteCronEntryResponse` for a finished operation: the empty ok payload
/// on success, the typed error otherwise. Failures travel in the payload, not
/// as a gRPC status (rules/proto.md).
#[must_use]
pub fn deleted_entry(result: Result<(), AgentError>) -> DeleteCronEntryResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(DeleteCronEntryOk {}),
        Err(error) => Outcome::Error(error),
    };

    DeleteCronEntryResponse {
        result: Some(result),
    }
}
