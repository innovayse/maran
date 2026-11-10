//! What a finished pull reports.

/// The outcome of a pull that ended well.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PullSummary {
    /// How many rsync runs the pull took.
    pub slices: u32,
    /// The size of the source home as measured before the first run, or `None`
    /// when the measurement failed (a pull does not depend on it).
    pub source_bytes: Option<u64>,
}
