//! The blocking side of a stream pump.

use tokio::sync::mpsc::Receiver;

use crate::services::wire::pump_message::PumpMessage;
use crate::services::wire::stream_ended::StreamEnded;

/// Delivers the chunks a [`StreamPump`](crate::services::wire::stream_pump::StreamPump)
/// forwards, and tells a clean end from a stream that stopped early.
///
/// Read on a `spawn_blocking` thread, so `blocking_recv` is the right wait. The
/// stream is complete only when the pump sent [`PumpMessage::Complete`]; a channel
/// that closes before it is [`StreamEnded`]. Each service wraps this in the source
/// type its operation takes, so the operation keeps its own marker for the abort.
pub struct ChunkReceiver {
    /// The pump's end of the bounded channel.
    receiver: Receiver<PumpMessage>,
    /// The chunk handed out last, which the receiver lends until the next call.
    current: Vec<u8>,
    /// Whether the terminator has been seen.
    complete: bool,
}

impl ChunkReceiver {
    /// Wraps the receiving end of the pump's channel.
    #[must_use]
    pub fn new(receiver: Receiver<PumpMessage>) -> Self {
        Self {
            receiver,
            current: Vec::new(),
            complete: false,
        }
    }

    /// Waits for the next non-empty chunk, `Ok(None)` after the terminator.
    ///
    /// The chunk borrows from the receiver and is valid until the next call.
    ///
    /// # Errors
    ///
    /// [`StreamEnded`] when the channel closed before the terminator arrived.
    pub fn next_chunk(&mut self) -> Result<Option<&[u8]>, StreamEnded> {
        if self.complete {
            return Ok(None);
        }
        loop {
            match self.receiver.blocking_recv() {
                Some(PumpMessage::Chunk(bytes)) if bytes.is_empty() => {}
                Some(PumpMessage::Chunk(bytes)) => {
                    self.current = bytes;
                    return Ok(Some(&self.current));
                }
                Some(PumpMessage::Complete) => {
                    self.complete = true;
                    return Ok(None);
                }
                None => return Err(StreamEnded),
            }
        }
    }
}

#[cfg(test)]
#[path = "../../tests/services/wire/chunk_receiver_tests.rs"]
mod tests;
