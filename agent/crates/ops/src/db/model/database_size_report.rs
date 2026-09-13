//! What one database occupies on disk.

/// The on-disk size of one database, in bytes.
///
/// A named type rather than a bare `u64` because the number is a quota input on
/// the panel's side, and a bare integer is the shape that gets passed to the
/// wrong parameter — this area also counts databases and error numbers.
///
/// The figure is the server's own accounting of data plus indexes. It is what
/// the server believes, not what the filesystem shows: the two differ after
/// deletions until the tablespace is reclaimed, and the server's figure is the
/// one that matches what the customer sees in every other tool.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DatabaseSizeReport {
    /// Data plus indexes, in bytes.
    pub bytes: u64,
}
