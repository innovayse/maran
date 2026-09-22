//! The one shape of refusal every site input check produces.

use crate::proto::{AgentError, ErrorCode};

/// Builds the wire error for input the agent refuses.
///
/// One function because every refusal in this area is the same answer with a
/// different sentence: the caller sent something the agent will not act on,
/// and the code is [`ErrorCode::InvalidInput`] in every case. Written once so
/// that a new check cannot classify a refusal differently from its neighbours.
#[must_use]
pub fn invalid_input(message: String) -> AgentError {
    AgentError {
        code: ErrorCode::InvalidInput as i32,
        message,
        tool_output: String::new(),
    }
}
