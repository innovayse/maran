//! A time to live, in seconds.

use crate::validation::dns::dns_ttl_error::DnsTtlError;

/// A TTL in `60..=604800` seconds (one minute to one week).
///
/// The floor keeps a customer from making every resolver on the internet ask this
/// server again each second; the ceiling keeps a mistake correctable within a
/// week. Zero is below the floor, which is also what an absent wire field decodes
/// to, so a caller that forgot to send a TTL is refused rather than published as
/// "do not cache".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DnsTtl(u32);

impl DnsTtl {
    /// The least TTL accepted.
    pub const MIN: u32 = 60;
    /// The greatest TTL accepted.
    pub const MAX: u32 = 604_800;

    /// Validates `candidate`.
    ///
    /// # Errors
    ///
    /// [`DnsTtlError::TooSmall`] below 60, [`DnsTtlError::TooLarge`] above 604800.
    pub fn parse(candidate: u32) -> Result<Self, DnsTtlError> {
        if candidate < Self::MIN {
            return Err(DnsTtlError::TooSmall { value: candidate });
        }

        if candidate > Self::MAX {
            return Err(DnsTtlError::TooLarge { value: candidate });
        }

        Ok(Self(candidate))
    }

    /// The TTL in seconds.
    #[must_use]
    pub fn seconds(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/dns_ttl_tests.rs"]
mod tests;
