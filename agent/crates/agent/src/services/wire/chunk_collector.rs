//! What a stream pump asks of the validator behind a client-streamed rpc.

use crate::proto::AgentError;

/// The validating state machine of one client-streamed rpc, seen from its pump.
///
/// A pump forwards only bodies: the opening message (the header) was read and
/// validated before the pump started, and every later message goes through here, so
/// the cap and the one-header rule hold for the whole stream. The two collectors
/// that exist (`WriteCollector`, `ImportCollector`) keep their own richer `accept`
/// for the opening message; this is the narrow face they show a pump.
pub trait ChunkCollector<R> {
    /// Judges one later message and answers the body bytes it carries, possibly
    /// none.
    ///
    /// # Errors
    ///
    /// The wire error for a message the stream's rules refuse; the pump stops at it.
    fn accept_chunk(&mut self, message: R) -> Result<Vec<u8>, AgentError>;
}
