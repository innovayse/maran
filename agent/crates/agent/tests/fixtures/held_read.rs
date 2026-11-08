//! A `ReadFile` stream left open.

#![allow(
    clippy::unwrap_used,
    clippy::expect_used,
    clippy::panic,
    dead_code,
    unused_imports
)]

use maran_agent::proto::ReadFileResponse;
use tonic::Streaming;

/// A `ReadFile` stream that has produced its first message and been left open.
///
/// Holding one keeps the browse slot of the call, because the agent's pump is
/// blocked on a full channel while nobody reads; dropping it ends the call.
pub struct HeldRead {
    /// The open stream.
    pub stream: Streaming<ReadFileResponse>,
}
