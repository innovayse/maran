//! The priority of an MX or SRV record.

use crate::validation::dns::record_priority_error::RecordPriorityError;

/// A 16-bit priority: the MX preference or the SRV priority, where the lower
/// value is tried first.
///
/// Zero is allowed — it is the highest priority and a real value, unlike a TTL.
/// The wire carries `uint32` (protobuf has no 16-bit integer), so the
/// out-of-range value exists on the wire and is refused here.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RecordPriority(u16);

impl RecordPriority {
    /// Validates `candidate`.
    ///
    /// # Errors
    ///
    /// [`RecordPriorityError::TooLarge`] above 65535.
    pub fn parse(candidate: u32) -> Result<Self, RecordPriorityError> {
        u16::try_from(candidate)
            .map(Self)
            .map_err(|_| RecordPriorityError::TooLarge { value: candidate })
    }

    /// The priority.
    #[must_use]
    pub fn value(&self) -> u16 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/record_priority_tests.rs"]
mod tests;
