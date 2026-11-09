//! Reading the opening of a write stream into a validated input, or refusing it.

use maran_ops::files::WriteFileInput;
use tonic::Streaming;

use crate::proto::{AgentError, WriteFileRequest};
use crate::services::files::write_collector::WriteCollector;
use crate::services::wire::invalid_input::invalid_input;

/// Reads the first message of the stream and returns what the operation should do,
/// the collector that keeps judging the rest, and the body bytes the first
/// message carried.
///
/// Deliberately thin: it pulls one message and hands it to [`WriteCollector`],
/// which holds every decision. A `tonic::Streaming` cannot be constructed by a
/// unit test, so anything decided in this function is decided where no test can
/// reach it; what is left here is the transport, exercised by the handshake test
/// over a real socket. Everything after the first message is forwarded by
/// `StreamPump`, never collected: the body is not held whole in the daemon.
///
/// # Errors
///
/// Returns the wire error when the transport fails, when the stream is empty, and
/// whatever [`WriteCollector::accept`] refuses.
pub async fn validated_write(
    stream: &mut Streaming<WriteFileRequest>,
) -> Result<(WriteFileInput, WriteCollector, Vec<u8>), AgentError> {
    let mut collector = WriteCollector::new();

    let first = match stream.message().await {
        Ok(Some(message)) => message,
        Ok(None) => {
            return Err(invalid_input(
                "the first message of a write stream carries the header".to_owned(),
            ));
        }
        Err(status) => {
            return Err(invalid_input(format!(
                "the write stream failed: {}",
                status.code()
            )));
        }
    };

    let accepted = collector.accept(first)?;
    let Some(input) = accepted.input else {
        return Err(invalid_input(
            "the first message of a write stream carries the header".to_owned(),
        ));
    };

    Ok((input, collector, accepted.chunk))
}
