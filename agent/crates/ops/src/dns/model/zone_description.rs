//! What the daemon says about one zone, with its signing.

use crate::dns::model::zone_signing_observation::ZoneSigningObservation;

/// The answer to a `GetZone`: whether the daemon serves the zone and, when it
/// does, at which serial and how it signs it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ZoneDescription {
    /// The daemon does not know the zone.
    NotServed,
    /// The daemon serves the zone.
    Served {
        /// The serial of the SOA the daemon holds.
        serial: u32,
        /// How the daemon signs it, as observed.
        signing: ZoneSigningObservation,
    },
}
