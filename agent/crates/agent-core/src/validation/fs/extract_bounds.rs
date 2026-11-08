//! The caps an extraction stated, inside the agent's own ceilings.

use super::archive_bounds_error::ArchiveBoundsError;

/// Most members any extraction may be allowed, whatever the request states.
///
/// Ten times the panel's configured default (100 000), so a panel tuned upward
/// keeps working and a request for a billion is refused.
pub const MAXIMUM_ENTRIES: u64 = 1_000_000;

/// Most bytes any extraction may be allowed to write (20 GiB).
pub const MAXIMUM_EXPANDED_BYTES: u64 = 20 * 1024 * 1024 * 1024;

/// Largest ratio of expanded bytes to archive bytes any extraction may allow.
pub const MAXIMUM_RATIO: u32 = 1000;

/// The three caps of one `ExtractArchive`, all required and all inside a ceiling.
///
/// Written forward from the question: *what stops a 40 KB file from becoming
/// 40 GB, a million empty files, or a quota?* The request states the allowance
/// (the panel owns the policy), the agent holds the ceiling (it owns the host),
/// and a type is the only way to hold either, so a bound cannot be forgotten
/// by a caller and cannot be zero.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ExtractBounds {
    /// The most members the archive may hold.
    max_entries: u64,
    /// The most bytes the extraction may write.
    max_expanded_bytes: u64,
    /// The most expanded bytes per archive byte, once a floor of expansion is passed.
    max_ratio: u32,
}

impl ExtractBounds {
    /// Builds the caps of one extraction.
    ///
    /// # Errors
    ///
    /// Returns [`ArchiveBoundsError::NoEntryCap`], [`ArchiveBoundsError::NoByteCap`]
    /// or [`ArchiveBoundsError::NoRatioCap`] for a zero, and
    /// [`ArchiveBoundsError::EntryCapTooHigh`], [`ArchiveBoundsError::ByteCapTooHigh`]
    /// or [`ArchiveBoundsError::RatioCapTooHigh`] for a value above the ceiling.
    pub fn parse(
        max_entries: u64,
        max_expanded_bytes: u64,
        max_ratio: u32,
    ) -> Result<Self, ArchiveBoundsError> {
        if max_entries == 0 {
            return Err(ArchiveBoundsError::NoEntryCap);
        }
        if max_expanded_bytes == 0 {
            return Err(ArchiveBoundsError::NoByteCap);
        }
        if max_ratio == 0 {
            return Err(ArchiveBoundsError::NoRatioCap);
        }
        if max_entries > MAXIMUM_ENTRIES {
            return Err(ArchiveBoundsError::EntryCapTooHigh);
        }
        if max_expanded_bytes > MAXIMUM_EXPANDED_BYTES {
            return Err(ArchiveBoundsError::ByteCapTooHigh);
        }
        if max_ratio > MAXIMUM_RATIO {
            return Err(ArchiveBoundsError::RatioCapTooHigh);
        }

        Ok(Self {
            max_entries,
            max_expanded_bytes,
            max_ratio,
        })
    }

    /// The most members the archive may hold.
    #[must_use]
    pub const fn max_entries(&self) -> u64 {
        self.max_entries
    }

    /// The most bytes the extraction may write.
    #[must_use]
    pub const fn max_expanded_bytes(&self) -> u64 {
        self.max_expanded_bytes
    }

    /// The most expanded bytes per archive byte.
    #[must_use]
    pub const fn max_ratio(&self) -> u32 {
        self.max_ratio
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/extract_bounds_tests.rs"]
mod tests;
