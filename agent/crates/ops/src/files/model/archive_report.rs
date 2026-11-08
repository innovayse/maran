//! What a finished archive operation reports.

/// The two figures an archive operation measured.
///
/// For an extraction: the members written and the bytes of file content written.
/// For a creation: the entries added and the size of the finished archive. Both
/// are counted by the child as it works, never taken from an archive's own
/// headers.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ArchiveReport {
    /// Members written (extraction) or entries added (creation).
    pub entries: u64,
    /// Bytes of file content written (extraction) or the archive's size (creation).
    pub bytes: u64,
}
