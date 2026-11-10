//! What a finished inventory read sent.

/// The counts of a complete read, for the terminal message of the stream.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct InventorySummary {
    /// How many accounts were sent.
    pub account_count: u32,
    /// How many warnings were sent.
    pub warning_count: u32,
}
