//! One thing an extraction made, so a failed run can take it away.

/// A file or a directory this run created, by its path below the home.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct CreatedEntry {
    /// The `/`-joined components below the account's home.
    pub(crate) path: String,
    /// Whether it is a directory (removed only if empty) or a file.
    pub(crate) is_directory: bool,
}
