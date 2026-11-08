//! The flags byte of a CAA record.

use crate::validation::dns::caa_flags_error::CaaFlagsError;

/// The two values of the CAA flags byte that mean anything: `0`, and `128`, the
/// critical bit.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CaaFlags {
    /// No flag set.
    None,
    /// The critical flag: an authority that does not understand the tag must
    /// refuse to issue.
    Critical,
}

impl CaaFlags {
    /// Parses the wire value.
    ///
    /// # Errors
    ///
    /// [`CaaFlagsError::Unknown`] for anything but `0` and `128`.
    pub fn parse(candidate: u32) -> Result<Self, CaaFlagsError> {
        match candidate {
            0 => Ok(Self::None),
            128 => Ok(Self::Critical),
            _ => Err(CaaFlagsError::Unknown { value: candidate }),
        }
    }

    /// The byte as the zone file spells it.
    #[must_use]
    pub fn value(&self) -> u8 {
        match self {
            Self::None => 0,
            Self::Critical => 128,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/caa_flags_tests.rs"]
mod tests;
