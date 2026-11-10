//! What was observed about the signing of one zone.

use crate::dns::model::key_publication::KeyPublication;
use crate::dns::model::signing_mode::SigningMode;

/// The daemon's own account of how it treats one zone, read from the daemon and
/// never from what the panel last sent (rules/rust.md "Config writes").
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ZoneSigningObservation {
    /// The mode the daemon's configuration for the zone states; `None` when it
    /// could not be read or was not understood.
    pub mode: Option<SigningMode>,
    /// The key the daemon reports having signed the zone with.
    pub key: KeyPublication,
}

impl ZoneSigningObservation {
    /// An observation that certifies nothing: nothing was read.
    pub const NOT_OBSERVED: Self = Self {
        mode: None,
        key: KeyPublication::NotObserved,
    };
}
