//! What a finished copy created.

/// The two figures a copy reports.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CopyReport {
    /// Entries created at the destination, the destination itself included.
    pub entries: u64,
    /// Bytes of file content copied, counted as they were written.
    pub bytes: u64,
}
