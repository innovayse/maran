//! What one complete `ReadFile` stream produced.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use maran_agent::proto::AgentError;

/// What one complete `ReadFile` stream produced.
pub struct ReadOutcome {
    /// Every byte the stream carried before it ended, in order.
    pub bytes: Vec<u8>,
    /// How many chunks carried them.
    pub chunks: usize,
    /// The error arm the stream ended with, if it did not end cleanly.
    pub error: Option<AgentError>,
}
