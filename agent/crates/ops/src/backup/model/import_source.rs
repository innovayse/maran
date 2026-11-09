//! Where the bytes of an import come from.

use crate::backup::model::import_interrupted::ImportInterrupted;

/// A stream of artifact chunks that is either finished or interrupted, and never
/// silently cut short.
///
/// The service layer implements it over the request stream (a bounded channel fed
/// by the async side). The end of a complete stream (`Ok(None)`) is told apart
/// from a stream that stopped before it was complete ([`ImportInterrupted`]); an
/// importer publishes only after the first and removes its stage after the second.
pub trait ImportSource {
    /// The next non-empty chunk, `Ok(None)` when the sender finished cleanly.
    ///
    /// The chunk borrows from the source and is valid until the next call.
    ///
    /// # Errors
    ///
    /// [`ImportInterrupted`] when the stream ended, broke or was dropped before
    /// it was complete.
    fn next_chunk(&mut self) -> Result<Option<&[u8]>, ImportInterrupted>;
}
