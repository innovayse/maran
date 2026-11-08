//! Forwarding the rest of a client-streamed rpc to its blocking operation, bounded.

use std::sync::{Arc, Mutex};
use std::time::Duration;

use tokio::sync::mpsc::channel;
use tokio::task::AbortHandle;
use tokio::time::timeout;
use tokio_stream::{Stream, StreamExt};
use tonic::Status;

use crate::proto::AgentError;
use crate::services::wire::chunk_collector::ChunkCollector;
use crate::services::wire::chunk_receiver::ChunkReceiver;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::pump_message::PumpMessage;
use crate::services::wire::stalled_upload::StalledUpload;

/// Chunks the channel holds before the pump waits.
///
/// Four chunks bound what the root daemon holds for one stream to four gRPC
/// messages, whatever the body's size or the client's speed (rules/rust.md "Async
/// and blocking": no unbounded buffering of streamed data). A full channel makes the
/// pump stop reading the stream, which is the client's back-pressure.
const CHANNEL_CHUNKS: usize = 4;

/// How long a client-streamed rpc may stay silent before the pump ends it.
///
/// Counted per message, from the moment the pump asks for the next one: a steady
/// stream, however slow, never reaches it, and a client that stops sending without
/// closing the stream does. Without it that client would hold the blocking
/// operation behind the stream, and the account lock the operation took, until the
/// connection dropped. Thirty seconds is far above the gap between two chunks of a
/// live upload (a chunk is a few hundred kilobytes) and short enough that a stalled
/// client frees its account within a minute.
const CHUNK_IDLE_LIMIT: Duration = Duration::from_secs(30);

/// The async half of a client-streamed rpc: owns the request stream, forwards what
/// the collector accepts, and remembers why it stopped if it had to.
///
/// One pump for every such rpc (`WriteFile`, `ImportBackup`). Two copies of the loop
/// had the same inputs, outputs, failure modes and bounds and differed in the
/// message type, the collector and one word of an error message, which are the
/// parameters here.
pub struct StreamPump {
    /// The refusal that ended the pump, if one did. Set BEFORE the channel closes, so
    /// a blocking side that sees the close can already read the reason.
    refusal: Arc<Mutex<Option<AgentError>>>,
    /// Stops the pump task when the operation is over, whichever way it ended.
    task: AbortHandle,
}

impl StreamPump {
    /// Starts forwarding `stream` and returns the receiver for the blocking operation.
    ///
    /// `first_chunk` is the body of the opening message, already accepted (empty for
    /// an rpc whose opening message carries none). Every later message goes through
    /// `collector`. A stream error or a refusal drops the channel without the
    /// terminator, so the operation sees an aborted stream and publishes nothing; a
    /// cleanly ended stream sends the terminator, and only then. A stream that sends
    /// nothing for `CHUNK_IDLE_LIMIT` is ended the same way, with a
    /// [`StalledUpload`] refusal. `what` names the stream in the message of a
    /// transport failure ("write", "import"). `stream` is a tonic `Streaming` in
    /// production; any stream of results serves a test.
    #[must_use]
    pub fn start<R, S, C>(
        stream: S,
        collector: C,
        first_chunk: Vec<u8>,
        what: &'static str,
    ) -> (ChunkReceiver, Self)
    where
        R: Send + 'static,
        S: Stream<Item = Result<R, Status>> + Unpin + Send + 'static,
        C: ChunkCollector<R> + Send + 'static,
    {
        let (sender, receiver) = channel(CHANNEL_CHUNKS);
        let refusal = Arc::new(Mutex::new(None));
        let stopped = Arc::clone(&refusal);

        let task = tokio::spawn(async move {
            let mut stream = stream;
            let mut collector = collector;
            let refused = async {
                if !first_chunk.is_empty()
                    && sender.send(PumpMessage::Chunk(first_chunk)).await.is_err()
                {
                    return None;
                }
                loop {
                    let Ok(next) = timeout(CHUNK_IDLE_LIMIT, stream.next()).await else {
                        return Some(AgentError::from(StalledUpload {
                            limit: CHUNK_IDLE_LIMIT,
                        }));
                    };
                    match next.transpose() {
                        Ok(Some(message)) => match collector.accept_chunk(message) {
                            Ok(chunk) => {
                                if !chunk.is_empty()
                                    && sender.send(PumpMessage::Chunk(chunk)).await.is_err()
                                {
                                    // The operation is gone and has its own answer.
                                    return None;
                                }
                            }
                            Err(error) => return Some(error),
                        },
                        Ok(None) => {
                            // A failed send means the operation already ended on its own.
                            let _ = sender.send(PumpMessage::Complete).await;
                            return None;
                        }
                        Err(status) => {
                            return Some(invalid_input(format!(
                                "the {what} stream failed: {}",
                                status.code()
                            )));
                        }
                    }
                }
            }
            .await;
            if let Some(error) = refused
                && let Ok(mut slot) = stopped.lock()
            {
                *slot = Some(error);
            }
            // `sender` drops here, after the reason is stored: a close without the
            // terminator is the abort the operation reads.
        });

        (
            ChunkReceiver::new(receiver),
            Self {
                refusal,
                task: task.abort_handle(),
            },
        )
    }

    /// Ends the pump and returns the refusal that stopped it, if any.
    ///
    /// A refusal the pump stopped with outranks the operation's failure, which is
    /// only its consequence (an aborted stream); a caller prefers it. The pump task is
    /// stopped either way, so a client that stalls after the operation is done cannot
    /// keep it alive.
    pub fn finish(self) -> Option<AgentError> {
        self.task.abort();
        self.refusal.lock().ok().and_then(|mut slot| slot.take())
    }
}

#[cfg(test)]
#[path = "../../tests/services/wire/stream_pump_tests.rs"]
mod tests;
