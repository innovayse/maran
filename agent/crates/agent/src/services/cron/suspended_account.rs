//! Turning the outcome of suspending or restoring an account's entries into the wire's response.

use crate::proto::{
    AgentError, SetAccountCronSuspendedOk, SetAccountCronSuspendedResponse,
    set_account_cron_suspended_response::Result as Outcome,
};

/// Builds the `SetAccountCronSuspendedResponse` for a finished operation: the empty ok payload
/// on success, the typed error otherwise. Failures travel in the payload, not
/// as a gRPC status (rules/proto.md).
#[must_use]
pub fn suspended_account(result: Result<(), AgentError>) -> SetAccountCronSuspendedResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(SetAccountCronSuspendedOk {}),
        Err(error) => Outcome::Error(error),
    };

    SetAccountCronSuspendedResponse {
        result: Some(result),
    }
}
