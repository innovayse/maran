//! The refusal of a destination this agent cannot reach.

use crate::proto::{AgentError, ErrorCode};

/// Builds the wire error for a destination the agent understands and cannot
/// serve.
///
/// **This is the only thing standing between an S3 destination and a backup
/// written to the local disk instead.** `ops::backup`'s four operations take a
/// `LocalBackupRoot` and nothing else — the object-store seam beneath them is
/// landed and has no caller — so a service that mapped every destination onto
/// that parameter would answer an operator who configured a bucket with a
/// successful local backup, and the panel would record it as stored off the
/// machine. That is the failure this function exists to make impossible, and
/// it is why the refusal lives at this boundary: down in `ops` the remote arm
/// cannot be NAMED, so a refusal there would be unreachable code.
///
/// [`ErrorCode::NotImplemented`] and not [`ErrorCode::InvalidInput`], because
/// the request is not invalid: the destination is well formed and the panel
/// was right to send it. What is missing is a code path in this build, which is
/// a different fact and calls for a different reaction — an operator changes
/// the destination or waits for an agent that has one, and a retry against
/// this agent will never do anything else.
#[must_use]
pub fn remote_destination_refused() -> AgentError {
    AgentError {
        code: ErrorCode::NotImplemented as i32,
        message: "this agent stores backups only on a local destination; \
                  the remote destination arm is not reachable in this build"
            .to_owned(),
        tool_output: String::new(),
    }
}
