//! The one shape of refusal every service's input checks produce.

use crate::proto::{AgentError, ErrorCode};

/// Builds the wire error for input the agent refuses.
///
/// One function because every refusal, in every service, is the same answer
/// with a different sentence: the caller sent something the agent will not act
/// on, and the code is [`ErrorCode::InvalidInput`] in every case. Written once
/// so that a new check cannot classify a refusal differently from its
/// neighbours, in its own service or in any other.
#[must_use]
pub fn invalid_input(message: String) -> AgentError {
    AgentError {
        code: ErrorCode::InvalidInput as i32,
        message,
        tool_output: String::new(),
    }
}
