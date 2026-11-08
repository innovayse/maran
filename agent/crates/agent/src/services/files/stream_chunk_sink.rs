//! The [`ChunkSink`] that puts a file's chunks onto the gRPC stream.

use std::time::Duration;

use maran_ops::files::model::chunk_sink::ChunkSink;
use maran_ops::files::model::sink_stopped::SinkStopped;
use tokio::sync::mpsc::Sender;
use tonic::Status;

use crate::proto::{AgentError, ReadFileChunk, ReadFileResponse, read_file_response};
use crate::services::wire::deadline_sender::DeadlineSender;
use crate::services::wire::send_end::SendEnd;
use crate::services::wire::sink_clock::SinkClock;

/// Delivers a read's chunks into the bounded channel behind the response stream.
///
/// The bound (a full channel makes the send wait, a closed one fails it, one that
/// stays full past the patience fails it too) lives in [`DeadlineSender`], shared
/// with the log tail. What this type adds is the file read's half of it: it
/// speaks the [`ChunkSink`] contract, and it REMEMBERS why it stopped. The
/// operation treats a stopped sink as "the reader left" and ends quietly, which
/// is right for a closed stream and wrong for a stalled one: a client that is
/// connected and not reading would, on resuming, see a stream that ends cleanly
/// after a prefix of the file. [`StreamChunkSink::ending`] lets the handler tell
/// the two apart and say so (see `read_terminal`).
pub struct StreamChunkSink {
    /// The bounded sender behind the response stream.
    sender: DeadlineSender<Result<ReadFileResponse, Status>>,
    /// Why the last send failed, if one did.
    ending: Option<SendEnd>,
}

impl StreamChunkSink {
    /// Creates the sink around the stream's sender.
    #[must_use]
    pub fn new(chunks: Sender<Result<ReadFileResponse, Status>>) -> Self {
        Self {
            sender: DeadlineSender::new(chunks),
            ending: None,
        }
    }

    /// The same sink with an explicit deadline AND an explicit clock, so the
    /// give-up path is observable without waiting it out. Production has one
    /// caller and it is [`Self::new`].
    #[must_use]
    pub fn with_patience_on_clock(
        chunks: Sender<Result<ReadFileResponse, Status>>,
        patience: Duration,
        clock: Box<dyn SinkClock>,
    ) -> Self {
        Self {
            sender: DeadlineSender::with_patience_on_clock(chunks, patience, clock),
            ending: None,
        }
    }

    /// Why the sink stopped delivering, or `None` while it has not.
    #[must_use]
    pub fn ending(&self) -> Option<SendEnd> {
        self.ending
    }

    /// Delivers the read's terminal error under the same deadline a chunk gets.
    ///
    /// Through THIS type and not a second clone of the sender, for the reason the
    /// log tail gives: the ending it is most often sent on is a stalled client,
    /// where a plain `blocking_send` has no bound at all. The outcome is
    /// deliberately discarded; what the bound buys is that the thread comes back.
    pub fn terminal(&mut self, error: AgentError) {
        let _ = self.sender.send(Ok(ReadFileResponse {
            result: Some(read_file_response::Result::Error(error)),
        }));
    }
}

impl ChunkSink for StreamChunkSink {
    /// Sends one chunk within the patience, and remembers the ending if it cannot.
    fn send(&mut self, chunk: &[u8]) -> Result<(), SinkStopped> {
        self.sender
            .send(Ok(ReadFileResponse {
                result: Some(read_file_response::Result::Ok(ReadFileChunk {
                    data: chunk.to_vec(),
                })),
            }))
            .map_err(|end| {
                self.ending = Some(end);
                SinkStopped
            })
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/stream_chunk_sink_tests.rs"]
mod tests;
