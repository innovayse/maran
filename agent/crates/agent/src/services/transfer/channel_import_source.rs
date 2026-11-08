//! The blocking importer's view of the stream pump.

use maran_ops::backup::{ImportInterrupted, ImportSource};

use crate::services::wire::chunk_receiver::ChunkReceiver;

/// Delivers the chunks the pump forwards, and tells a clean end from a stream that
/// stopped early.
///
/// The wait and the complete-only-with-the-terminator rule are [`ChunkReceiver`]'s,
/// shared with the file upload. A channel that closes without the terminator is the
/// abort: the pump drops its sender on a refusal or a stream error, and the importer
/// reads [`ImportInterrupted`] and must publish nothing.
pub struct ChannelImportSource {
    /// The pump's end of the bounded channel.
    chunks: ChunkReceiver,
}

impl ChannelImportSource {
    /// Wraps the receiver a pump made.
    #[must_use]
    pub fn new(chunks: ChunkReceiver) -> Self {
        Self { chunks }
    }
}

impl ImportSource for ChannelImportSource {
    /// Waits for the next non-empty chunk, `Ok(None)` after the terminator.
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, ImportInterrupted> {
        self.chunks.next_chunk().map_err(|_ended| ImportInterrupted)
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/channel_import_source_tests.rs"]
mod tests;
