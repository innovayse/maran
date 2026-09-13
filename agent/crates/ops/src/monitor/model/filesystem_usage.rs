//! How full a filesystem is.

/// Space used and space installed on one filesystem.
///
/// "Used" is what the filesystem itself accounts for, not the sum of the files
/// on it: the two differ by the reserved blocks only root may write, by
/// metadata, and by every file some process still holds open after unlinking.
/// A hosting operator wants the first number, because it is the one that
/// decides when writes start failing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FilesystemUsage {
    /// Bytes occupied.
    pub used_bytes: u64,
    /// Bytes the filesystem holds in total.
    pub total_bytes: u64,
}
