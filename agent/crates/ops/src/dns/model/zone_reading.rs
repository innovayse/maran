//! What the daemon says about one zone.

/// The daemon's answer to "do you serve this zone, and at which serial".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ZoneReading {
    /// The daemon does not know the zone.
    NotServed,
    /// The daemon serves the zone at this serial.
    Served {
        /// The serial of the SOA the daemon holds.
        serial: u32,
    },
}
