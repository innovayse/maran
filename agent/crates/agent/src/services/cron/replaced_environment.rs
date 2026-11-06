//! Turning the outcome of replacing the managed environment into the wire's response.

use crate::proto::{
    AgentError, SetCronEnvironmentOk, SetCronEnvironmentResponse,
    set_cron_environment_response::Result as Outcome,
};

/// Builds the `SetCronEnvironmentResponse` for a finished operation: the empty ok payload
/// on success, the typed error otherwise. Failures travel in the payload, not
/// as a gRPC status (rules/proto.md).
#[must_use]
pub fn replaced_environment(result: Result<(), AgentError>) -> SetCronEnvironmentResponse {
    let result = match result {
        Ok(()) => Outcome::Ok(SetCronEnvironmentOk {}),
        Err(error) => Outcome::Error(error),
    };

    SetCronEnvironmentResponse {
        result: Some(result),
    }
}
