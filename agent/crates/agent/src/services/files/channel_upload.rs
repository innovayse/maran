//! The blocking writer's view of the stream pump.

use maran_ops::files::model::upload_aborted::UploadAborted;
use maran_ops::files::model::upload_source::UploadSource;

use crate::services::wire::chunk_receiver::ChunkReceiver;

/// An [`UploadSource`] fed by the async pump through a bounded channel.
///
/// The wait and the complete-only-with-the-terminator rule are
/// [`ChunkReceiver`]'s, shared with the import; this is the marker the writer
/// reads an early end by. A channel that closes before the terminator (the pump met
/// an error, or the client went away and the pump was torn down) is
/// [`UploadAborted`], and the writer removes its temporary file.
pub struct ChannelUpload {
    /// The pump's end of the bounded channel.
    chunks: ChunkReceiver,
}

impl ChannelUpload {
    /// Wraps the receiver a pump made.
    #[must_use]
    pub fn new(chunks: ChunkReceiver) -> Self {
        Self { chunks }
    }
}

impl UploadSource for ChannelUpload {
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        self.chunks.next_chunk().map_err(|_ended| UploadAborted)
    }
}

#[cfg(test)]
#[path = "../../tests/services/files/channel_upload_tests.rs"]
mod tests;
