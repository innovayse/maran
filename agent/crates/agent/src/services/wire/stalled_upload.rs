//! The ending of a client-streamed rpc whose client stopped sending without closing.

use std::time::Duration;

use crate::proto::{AgentError, ErrorCode};

/// A client-streamed rpc received no message for [`Duration`] `limit`, so the agent
/// stopped waiting for one.
///
/// A client that goes quiet without closing its stream or disconnecting would hold
/// the blocking operation behind the stream (and the account lock that operation
/// took) for as long as the connection stays open. The pump ends the stream instead;
/// the operation then sees an aborted stream, publishes nothing and lets go of the
/// lock. The wire code is [`ErrorCode::StreamDropped`], which says the agent ended
/// the stream to free its worker, is not a fault of the operation, and may be retried
/// by sending the stream again (the same meaning it has for a response stream whose
/// client stopped reading).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StalledUpload {
    /// How long the stream had been silent when the agent gave up.
    pub limit: Duration,
}

impl From<StalledUpload> for AgentError {
    /// The wire error for a stalled upload; it names the silence, never the stream's content.
    fn from(stalled: StalledUpload) -> Self {
        Self {
            code: ErrorCode::StreamDropped as i32,
            message: format!(
                "the upload was dropped: the client sent nothing for {} seconds",
                stalled.limit.as_secs()
            ),
            tool_output: String::new(),
        }
    }
}

#[cfg(test)]
#[path = "../../tests/services/wire/stalled_upload_tests.rs"]
mod tests;
