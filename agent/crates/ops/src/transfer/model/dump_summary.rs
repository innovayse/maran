//! What a finished database transfer reports.

/// The outcome of a database transfer that ended well.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DumpSummary {
    /// How many bytes of dump were received and loaded.
    pub dump_bytes: u64,
}
