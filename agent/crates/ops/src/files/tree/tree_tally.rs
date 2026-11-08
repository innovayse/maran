//! What a walk of a tree counted.

/// The figures a walk accumulates, filled in as it goes.
///
/// `unsupported`, `crossings` and `foreign` are counted and not raised, because
/// what they mean depends on the operation: a copy refuses the whole tree if
/// any is non-zero, while a permission change leaves those entries alone and
/// reports them.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub(crate) struct TreeTally {
    /// Entries visited, the root included.
    pub(crate) entries: u64,
    /// Bytes of regular-file content.
    pub(crate) bytes: u64,
    /// Symlinks, FIFOs, sockets and devices, which are never opened.
    pub(crate) unsupported: u64,
    /// Directories on another filesystem than the root, which are never entered.
    pub(crate) crossings: u64,
    /// Directories the account does not own, which are never entered.
    pub(crate) foreign: u64,
}
