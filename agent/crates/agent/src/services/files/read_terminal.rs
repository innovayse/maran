//! What, if anything, a finished file read owes its client.

use maran_ops::files::FilesOpError;

use crate::proto::{AgentError, ErrorCode};
use crate::services::files::file_status::to_agent_error;
use crate::services::wire::send_end::SendEnd;

/// The terminal error for a read that has ended, or `None` when the stream
/// already ended the way the client expects.
///
/// Three endings need a decision. A failed read is its operation's typed error.
/// A read whose sink stalled ended "successfully" from the operation's point of
/// view (the reader left) while the client is still connected, so without a
/// message it would see a clean end after a prefix of the file; it is told the
/// stream was dropped. A read whose client closed the stream has nobody to tell.
/// A read that completed has nothing to say: the end of the stream is the
/// answer.
///
/// A separate unit because it is a decision with four cases and a handler is a
/// translation layer (rules/rust.md "Service anatomy").
#[must_use]
pub fn read_terminal(
    outcome: &Result<u64, FilesOpError>,
    ending: Option<SendEnd>,
) -> Option<AgentError> {
    match (outcome, ending) {
        (Err(error), _) => Some(to_agent_error(error)),
        (Ok(_), Some(SendEnd::Stalled)) => Some(AgentError {
            code: ErrorCode::StreamDropped as i32,
            message: "the file stream was dropped: the client stopped reading it".to_owned(),
            tool_output: String::new(),
        }),
        (Ok(_), Some(SendEnd::Closed) | None) => None,
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/read_terminal_tests.rs"]
mod tests;
