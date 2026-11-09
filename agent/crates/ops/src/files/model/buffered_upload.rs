//! An upload whose whole body is already in memory.

use crate::files::model::upload_aborted::UploadAborted;
use crate::files::model::upload_source::UploadSource;

/// A body held in memory, handed out as one chunk and then reported complete.
///
/// For callers that have a small fixed body (and for tests); an upload read from a client
/// streams instead of being held whole.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BufferedUpload {
    /// The body, until it has been handed out.
    body: Vec<u8>,
    /// Whether the body has been handed out.
    delivered: bool,
}

impl BufferedUpload {
    /// An upload of exactly `body`.
    #[must_use]
    pub fn new(body: Vec<u8>) -> Self {
        Self {
            body,
            delivered: false,
        }
    }
}

impl UploadSource for BufferedUpload {
    /// The whole body once (nothing, for an empty one), then `Ok(None)`.
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted> {
        if self.delivered || self.body.is_empty() {
            return Ok(None);
        }
        self.delivered = true;
        Ok(Some(&self.body))
    }
}

#[cfg(test)]
#[path = "../../tests/files/model/buffered_upload_tests.rs"]
mod tests;
