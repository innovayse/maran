//! How far reading one log file got.

/// Where a bounded read of one file ended.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ReadProgress {
    /// The offset of the first byte not yet counted; always at a line boundary
    /// unless the file ended on a partial line that was flushed.
    pub offset: u64,
    /// `true` when the read reached the end the file had when it started;
    /// `false` when the byte budget stopped it first.
    pub finished: bool,
}
