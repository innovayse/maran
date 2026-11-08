//! The weight of an SRV record.

use crate::validation::dns::srv_weight_error::SrvWeightError;

/// A 16-bit SRV weight, used to share load among targets of equal priority.
/// Zero is a real value (no preference).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SrvWeight(u16);

impl SrvWeight {
    /// Validates `candidate`.
    ///
    /// # Errors
    ///
    /// [`SrvWeightError::TooLarge`] above 65535.
    pub fn parse(candidate: u32) -> Result<Self, SrvWeightError> {
        u16::try_from(candidate)
            .map(Self)
            .map_err(|_| SrvWeightError::TooLarge { value: candidate })
    }

    /// The weight.
    #[must_use]
    pub fn value(&self) -> u16 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/srv_weight_tests.rs"]
mod tests;
