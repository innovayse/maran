//! Where a backup creation's progress goes.

use crate::backup::model::backup_stage::BackupStage;

/// The receiving end of a creation's progress.
///
/// A trait and not a `FnMut` callback, because a sink is held across a whole
/// operation and the service layer's implementation carries state (the bounded
/// channel the stream drains). It is deliberately infallible: a client that has
/// stopped listening does not make a backup that is already halfway through
/// dumping a database into something worth abandoning, and an operation that
/// could fail on a progress report would have a failure path per report.
///
/// Implementations MUST return promptly. This is called from the operation's
/// own thread, between two pieces of real work, so a sink that blocks stalls
/// the backup rather than the reader.
pub trait ProgressSink {
    /// Reports that `stage` is `percent` of the way through the whole
    /// operation.
    ///
    /// `percent` is always computed by [`BackupStage::percent_through`] or is
    /// the stage's own boundary; no caller writes a number down.
    fn report(&mut self, stage: BackupStage, percent: u32);
}
