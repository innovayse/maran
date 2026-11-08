//! Where the bytes of a streamed read go.

use super::sink_stopped::SinkStopped;

/// Receives a file's content one chunk at a time.
///
/// A trait because the operation must not know about gRPC: the service layer
/// supplies a sink that forwards each chunk onto a bounded channel, and a test
/// supplies one that records them. Bounded by construction — a sink takes a
/// chunk at a time and may make the sender wait, so a client that reads slowly
/// slows the read instead of making the agent buffer a customer's file.
pub trait ChunkSink {
    /// Delivers one chunk, in order.
    ///
    /// # Errors
    ///
    /// Returns [`SinkStopped`] when the reader is gone or too slow to wait for.
    fn send(&mut self, chunk: &[u8]) -> Result<(), SinkStopped>;
}
