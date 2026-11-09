//! What a permission change reached and what it left alone.

/// The two figures a permission change reports.
///
/// A non-zero `skipped` tells the caller the change did not reach everything it
/// asked for; it is a report and never an error.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct PermissionReport {
    /// Entries whose mode was set, including ones that already had it.
    pub changed: u64,
    /// Entries left alone: a symlink, an entry the account does not own, a
    /// directory on another filesystem, or one that could not be opened.
    pub skipped: u64,
}
