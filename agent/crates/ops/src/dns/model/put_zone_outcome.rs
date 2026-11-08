//! What a completed `put_zone` did.

/// The result of replacing one zone.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PutZoneOutcome {
    /// True when the daemon did not know the zone before the call.
    pub created: bool,
    /// The serial the daemon reports after the call, read back from it.
    pub serial: u32,
}
