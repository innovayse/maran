//! Draining a write stream into a validated input, or refusing it.

use maran_ops::files::WriteFileInput;
use tonic::Streaming;

use crate::proto::{AgentError, WriteFileRequest};
use crate::services::files::write_collector::WriteCollector;
use crate::services::wire::invalid_input::invalid_input;

/// Reads the whole write stream and returns what the operation should do.
///
/// Deliberately thin: it pulls messages and hands each to [`WriteCollector`],
/// which holds every decision. A `tonic::Streaming` cannot be constructed by a
/// unit test, so anything decided in this function is decided where no test can
/// reach it — which is how the header rules and the byte cap came to be
/// unobserved in the first version. What is left here is the transport, and the
/// transport is exercised by the handshake test over a real socket.
///
/// # Errors
///
/// Returns the wire error when the transport fails mid-stream, and whatever
/// [`WriteCollector::accept`] or [`WriteCollector::finish`] refuses.
pub async fn validated_write(
    mut stream: Streaming<WriteFileRequest>,
) -> Result<WriteFileInput, AgentError> {
    let mut collector = WriteCollector::new();

    loop {
        match stream.message().await {
            Ok(Some(message)) => collector.accept(message)?,
            Ok(None) => break,
            Err(status) => {
                return Err(invalid_input(format!(
                    "the write stream failed: {}",
                    status.code()
                )));
            }
        }
    }

    collector.finish()
}
