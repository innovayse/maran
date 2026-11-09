//! A refresh, retry or expire interval of a zone's SOA record.

use crate::validation::dns::soa_interval_error::SoaIntervalError;

/// An interval in `60..=2419200` seconds (one minute to four weeks).
///
/// Distinct from [`super::dns_ttl::DnsTtl`] because it means something else — how
/// long a secondary waits, not how long a resolver caches — and its ceiling is
/// four weeks, which is a normal `expire`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SoaInterval(u32);

impl SoaInterval {
    /// The least interval accepted.
    pub const MIN: u32 = 60;
    /// The greatest interval accepted.
    pub const MAX: u32 = 2_419_200;

    /// Validates `candidate`.
    ///
    /// # Errors
    ///
    /// [`SoaIntervalError::TooSmall`] below 60, [`SoaIntervalError::TooLarge`]
    /// above 2419200.
    pub fn parse(candidate: u32) -> Result<Self, SoaIntervalError> {
        if candidate < Self::MIN {
            return Err(SoaIntervalError::TooSmall { value: candidate });
        }

        if candidate > Self::MAX {
            return Err(SoaIntervalError::TooLarge { value: candidate });
        }

        Ok(Self(candidate))
    }

    /// The interval in seconds.
    #[must_use]
    pub fn seconds(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/soa_interval_tests.rs"]
mod tests;
