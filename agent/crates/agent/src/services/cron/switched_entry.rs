//! Turning the outcome of switching an entry on or off into the wire's response.

use crate::proto::{
    AgentError, SetCronEntryEnabledOk, SetCronEntryEnabledResponse,
    set_cron_entry_enabled_response::Result as Outcome,
};

/// Builds the `SetCronEntryEnabledResponse` for a finished operation: the empty ok payload
/// on success, the typed error otherwise. Failures travel in the payload, not
/// as a gRPC status (rules/proto.md).
#[must_use]
pub fn switched_entry(result: Result<(), AgentError>) -> SetCronEntryEnabledResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(SetCronEntryEnabledOk {}),
        Err(error) => Outcome::Error(error),
    };

    SetCronEntryEnabledResponse {
        result: Some(result),
    }
}
