//! Where a pull reports its progress, and how it learns the client left.

/// The receiving end of a pull's progress.
///
/// A trait so the pull is driven against a recording sink in tests and so the
/// gRPC stream stays the service's business. The pull is cancellation class
/// "stop": it asks [`Self::is_open`] between slices and ends when the client is
/// gone.
pub trait PullSink {
    /// Reports that `slices_completed` rsync runs have ended and another will
    /// run.
    fn progress(&mut self, slices_completed: u32);

    /// Whether the client is still there to hear the pull.
    fn is_open(&self) -> bool;
}
