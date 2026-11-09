//! What a completed `delete_zone` did.

/// The result of removing one zone.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DeleteZoneOutcome {
    /// True when the zone was served or had a file before the call; false when
    /// there was nothing to remove, the repeat of a delete.
    pub was_present: bool,
}
