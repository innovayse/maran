//! The one constructor for the wire's SystemFailure error.

use crate::proto::{AgentError, ErrorCode};

/// Wraps an agent-side breakdown — a panic in a blocking task, a subsystem
/// that did not answer — as the wire error the panel reports as a fault.
///
/// One constructor rather than a literal at each call site, so the code, the
/// empty `tool_output` and the shape cannot drift apart between services.
#[must_use]
pub fn system_failure(message: String) -> AgentError {
    AgentError {
        code: ErrorCode::SystemFailure as i32,
        message,
        tool_output: String::new(),
    }
}
