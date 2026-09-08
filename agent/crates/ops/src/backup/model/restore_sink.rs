//! Where a restore's progress goes.

use crate::backup::model::restore_stage::RestoreStage;

/// The receiving end of a restore's progress.
///
/// A second trait beside
/// [`crate::backup::model::progress_sink::ProgressSink`] rather than one
/// generic over the stage, for the same reason [`RestoreStage`] is a second
/// enum: the two operations report different work, and a sink typed to the
/// restore's stages cannot be handed a creation's.
///
/// Infallible, like its sibling: a client that has stopped listening does not
/// make a restore that has already dropped a database into something worth
/// abandoning, and an operation that could fail on a progress report would have
/// a failure path per report — one of them inside the window between the two
/// renames.
///
/// Implementations MUST return promptly. This is called from the operation's
/// own thread, between two pieces of real work, so a sink that blocks stalls
/// the restore rather than the reader.
pub trait RestoreSink {
    /// Reports that `stage` is `percent` of the way through the whole
    /// operation.
    ///
    /// `percent` is always computed by [`RestoreStage::percent_through`] or is
    /// the stage's own boundary; no caller writes a number down.
    fn report(&mut self, stage: RestoreStage, percent: u32);
}
