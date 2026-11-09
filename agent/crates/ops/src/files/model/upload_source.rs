//! Where the bytes of an upload come from.

use crate::files::model::upload_aborted::UploadAborted;

/// A stream of chunks that is either finished or aborted, and never silently cut short.
///
/// The parent implements it over the request stream (a bounded channel fed by the async side)
/// and the forked child implements it over the framed channel from the parent. Both distinguish
/// the end of a complete upload (`Ok(None)`) from a stream that stopped before it was
/// complete ([`UploadAborted`]); a writer publishes a file only after the first and removes its
/// temporary file after the second.
pub trait UploadSource {
    /// The next non-empty chunk, `Ok(None)` when the upload is complete.
    ///
    /// The chunk borrows from the source and is valid until the next call.
    ///
    /// # Errors
    ///
    /// Returns [`UploadAborted`] when the stream ended, broke or was dropped before it was
    /// complete.
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, UploadAborted>;
}
