//! What the daemon reports about the key that signed a zone.

/// The daemon's answer to "which key has finished signing this zone".
///
/// Three values and not an `Option<u16>`, for the reason
/// [`DnsPolicyObservation`](crate::dns::DnsPolicyObservation) is three-valued: a
/// daemon that was not asked, or whose answer was not understood, must not read
/// as one that reported nothing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum KeyPublication {
    /// The daemon was not asked, could not be, or its answer was not understood.
    NotObserved,
    /// The daemon reports no completed signing of the zone.
    NotPublished,
    /// The daemon reports a completed signing by the key with this tag.
    Published(u16),
}
