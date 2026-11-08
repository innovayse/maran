//! The most body bytes one upload may carry.

use super::upload_limit_error::UploadLimitError;

/// The byte cap of one upload, valid by construction.
///
/// Zero means "not stated" and becomes [`UploadLimit::DEFAULT_BYTES`], so an old caller is unchanged;
/// above [`UploadLimit::CEILING_BYTES`] is refused and never clamped, so a caller that asks for more
/// than the agent allows learns it instead of being silently cut short.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct UploadLimit(u64);

impl UploadLimit {
    /// What a caller that states no cap gets: one mebibyte, the original limit of `WriteFile`.
    ///
    /// The only caller that predates the cap, the ACME challenge write, sends a key
    /// authorization under a hundred bytes and states nothing, so it keeps exactly the bound it
    /// always had.
    pub const DEFAULT_BYTES: u64 = 1024 * 1024;

    /// The most the agent accepts for one upload whatever the request states: 256 MiB.
    pub const CEILING_BYTES: u64 = 256 * 1024 * 1024;

    /// Accepts the cap a request stated.
    ///
    /// # Errors
    ///
    /// Returns [`UploadLimitError::AboveCeiling`] when `stated` exceeds the ceiling.
    pub fn parse(stated: u64) -> Result<Self, UploadLimitError> {
        match stated {
            0 => Ok(Self(Self::DEFAULT_BYTES)),
            over if over > Self::CEILING_BYTES => Err(UploadLimitError::AboveCeiling),
            within => Ok(Self(within)),
        }
    }

    /// The cap in bytes.
    #[must_use]
    pub fn bytes(self) -> u64 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/upload_limit_tests.rs"]
mod tests;
