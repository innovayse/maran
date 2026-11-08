//! What a log prune removed.

/// The outcome of one prune.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PrunedLogs {
    /// Archive files removed by this call.
    pub removed_files: u32,
}
