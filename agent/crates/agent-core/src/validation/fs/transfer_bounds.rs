//! The stated allowances of a copy.

use super::transfer_bounds_error::TransferBoundsError;

/// How many entries and bytes a copy may hold, as the request stated them.
///
/// Both are required: zero is refused, so `LIMIT_REACHED` can only ever mean an
/// allowance the caller named was reached, and a request cannot opt out of the
/// bound by leaving it unset. Above the ceilings is refused, not clamped.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TransferBounds {
    /// The most entries, files and directories together.
    max_entries: u64,
    /// The most bytes of file content.
    max_bytes: u64,
}

impl TransferBounds {
    /// Most entries a request may state: ten times the panel's default of 100,000.
    pub const CEILING_ENTRIES: u64 = 1_000_000;

    /// Most bytes a request may state: ten times the panel's default of 2 GiB.
    pub const CEILING_BYTES: u64 = 20 * 1024 * 1024 * 1024;

    /// Accepts the two stated caps.
    ///
    /// # Errors
    ///
    /// Returns [`TransferBoundsError::NotStated`] when either is zero and
    /// [`TransferBoundsError::AboveCeiling`] when either exceeds its ceiling.
    pub fn parse(max_entries: u64, max_bytes: u64) -> Result<Self, TransferBoundsError> {
        if max_entries == 0 || max_bytes == 0 {
            return Err(TransferBoundsError::NotStated);
        }
        if max_entries > Self::CEILING_ENTRIES || max_bytes > Self::CEILING_BYTES {
            return Err(TransferBoundsError::AboveCeiling);
        }
        Ok(Self {
            max_entries,
            max_bytes,
        })
    }

    /// The most entries the copy may hold.
    #[must_use]
    pub fn max_entries(&self) -> u64 {
        self.max_entries
    }

    /// The most bytes the copy may hold.
    #[must_use]
    pub fn max_bytes(&self) -> u64 {
        self.max_bytes
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/transfer_bounds_tests.rs"]
mod tests;
