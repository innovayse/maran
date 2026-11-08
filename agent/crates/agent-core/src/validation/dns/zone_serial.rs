//! The serial of a zone's SOA record.

use crate::validation::dns::zone_serial_error::ZoneSerialError;

/// A non-zero SOA serial.
///
/// The panel computes the serial and sends it; the agent never derives one. Zero
/// is refused because it is what an absent wire field decodes to, and a zone
/// published with a serial nobody chose would never be seen as newer by a
/// secondary.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ZoneSerial(u32);

impl ZoneSerial {
    /// Validates `candidate`.
    ///
    /// # Errors
    ///
    /// [`ZoneSerialError::Zero`] for zero.
    pub fn parse(candidate: u32) -> Result<Self, ZoneSerialError> {
        if candidate == 0 {
            return Err(ZoneSerialError::Zero);
        }

        Ok(Self(candidate))
    }

    /// The serial.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/zone_serial_tests.rs"]
mod tests;
