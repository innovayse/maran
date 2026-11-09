//! What the async side of a client-streamed rpc sends to the blocking side.

/// One thing on the channel between a stream pump and the blocking operation it feeds.
///
/// The stream ending is a message of its own, and a channel that closes without it
/// is NOT a finished stream: that difference is what keeps a dropped client from
/// publishing a half-written file or a half-received package.
pub enum PumpMessage {
    /// Body bytes, in stream order.
    Chunk(Vec<u8>),
    /// The client finished its stream cleanly.
    Complete,
}
