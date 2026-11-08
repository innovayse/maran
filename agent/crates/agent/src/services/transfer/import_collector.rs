//! Validating a client-streamed import, one message at a time.

use maran_ops::backup::ImportRequest;
use maran_ops::backup::package::signed_manifest::SignedManifest;

use crate::proto::{AgentError, ImportBackupRequest, import_backup_request};
use crate::services::transfer::accepted::Accepted;
use crate::services::wire::chunk_collector::ChunkCollector;
use crate::services::wire::invalid_input::invalid_input;

/// The state machine behind the stream: the header comes first, exactly once, and
/// every later message is a chunk.
///
/// Held apart from the transport so a unit test can drive it: a tonic `Streaming`
/// cannot be constructed by one. It enforces only the shape of the stream. The
/// BYTE bound is the operation's, not this type's: the size a stream may carry is
/// the one the signed manifest declares, and a manifest is not believed until its
/// signature has verified, which happens inside `ops` — so the operation refuses
/// the first byte past the declared length, and the pump's bounded channel keeps
/// what is held in memory at a few chunks meanwhile.
pub struct ImportCollector {
    /// Whether the header has been accepted.
    opened: bool,
}

impl ImportCollector {
    /// A collector awaiting the header.
    #[must_use]
    pub fn new() -> Self {
        Self { opened: false }
    }

    /// Accepts one message.
    ///
    /// # Errors
    ///
    /// `INVALID_INPUT` for a message with no part, a first message that is not the
    /// header, and a header after the first message.
    pub fn accept(&mut self, message: ImportBackupRequest) -> Result<Accepted, AgentError> {
        match (self.opened, message.part) {
            (false, Some(import_backup_request::Part::Header(header))) => {
                self.opened = true;
                Ok(Accepted {
                    header: Some(ImportRequest {
                        signed: SignedManifest {
                            manifest: header.manifest,
                            signature: header.signature,
                            public_key: header.public_key,
                        },
                        expected_fingerprint: header.expected_key_fingerprint,
                        max_bytes: header.max_bytes,
                    }),
                    chunk: Vec::new(),
                })
            }
            (false, _) => Err(invalid_input(
                "the first message of an import stream carries the header".to_owned(),
            )),
            (true, Some(import_backup_request::Part::Header(_))) => Err(invalid_input(
                "the header belongs on the first message of an import stream, and there is \
                 exactly one of it"
                    .to_owned(),
            )),
            (true, Some(import_backup_request::Part::Chunk(chunk))) => Ok(Accepted {
                header: None,
                chunk,
            }),
            (true, None) => Err(invalid_input(
                "a message of an import stream carries a header or a chunk".to_owned(),
            )),
        }
    }
}

impl ChunkCollector<ImportBackupRequest> for ImportCollector {
    /// The chunk of a later message, judged by [`ImportCollector::accept`]: a second
    /// header or an empty message ends the stream.
    fn accept_chunk(&mut self, message: ImportBackupRequest) -> Result<Vec<u8>, AgentError> {
        self.accept(message).map(|accepted| accepted.chunk)
    }
}

impl Default for ImportCollector {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/import_collector_tests.rs"]
mod tests;
