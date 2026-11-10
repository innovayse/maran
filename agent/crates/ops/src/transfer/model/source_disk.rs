//! The room on the filesystem that holds a source's homes.

/// Free and total bytes of the filesystem under the source's homes, as the
/// source reported them.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SourceDisk {
    /// Bytes available to an unprivileged writer.
    pub free_bytes: u64,
    /// Total bytes of the filesystem.
    pub total_bytes: u64,
}
