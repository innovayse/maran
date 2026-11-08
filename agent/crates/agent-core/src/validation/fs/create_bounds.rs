//! The caps an archive creation stated, inside the agent's own ceilings.

use super::archive_bounds_error::ArchiveBoundsError;
use super::extract_bounds::{MAXIMUM_ENTRIES, MAXIMUM_EXPANDED_BYTES};

/// The two caps of one `CreateArchive`, both required and both inside a ceiling.
///
/// The ceilings are the extraction's: an archive the agent will make is one it
/// would also unpack. There is no ratio, because the sources are read from the
/// account's own tree and nothing is expanded.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CreateBounds {
    /// The most entries (files and directories) the sources may hold together.
    max_entries: u64,
    /// The most bytes of file content the archive may be made from.
    max_source_bytes: u64,
}

impl CreateBounds {
    /// Builds the caps of one creation.
    ///
    /// # Errors
    ///
    /// Returns [`ArchiveBoundsError::NoEntryCap`] or [`ArchiveBoundsError::NoByteCap`]
    /// for a zero, and [`ArchiveBoundsError::EntryCapTooHigh`] or
    /// [`ArchiveBoundsError::ByteCapTooHigh`] for a value above the ceiling.
    pub fn parse(max_entries: u64, max_source_bytes: u64) -> Result<Self, ArchiveBoundsError> {
        if max_entries == 0 {
            return Err(ArchiveBoundsError::NoEntryCap);
        }
        if max_source_bytes == 0 {
            return Err(ArchiveBoundsError::NoByteCap);
        }
        if max_entries > MAXIMUM_ENTRIES {
            return Err(ArchiveBoundsError::EntryCapTooHigh);
        }
        if max_source_bytes > MAXIMUM_EXPANDED_BYTES {
            return Err(ArchiveBoundsError::ByteCapTooHigh);
        }

        Ok(Self {
            max_entries,
            max_source_bytes,
        })
    }

    /// The most entries the sources may hold together.
    #[must_use]
    pub const fn max_entries(&self) -> u64 {
        self.max_entries
    }

    /// The most bytes of file content the archive may be made from.
    #[must_use]
    pub const fn max_source_bytes(&self) -> u64 {
        self.max_source_bytes
    }
}

#[cfg(test)]
#[path = "../../tests/validation/fs/create_bounds_tests.rs"]
mod tests;
