//! What an archive operation has used of the allowance its request stated.

use maran_agent_core::validation::fs::create_bounds::CreateBounds;
use maran_agent_core::validation::fs::extract_bounds::ExtractBounds;

use super::archive_refusal::ArchiveRefusal;

/// Expansion below this many bytes is never judged by the ratio.
///
/// A small, legitimately compressible archive (a megabyte of zeros is a few
/// hundred bytes of deflate) must not be refused as a bomb. Above it the ratio
/// decides, and the byte cap still decides regardless.
const RATIO_FLOOR: u64 = 1024 * 1024;

/// Bytes of tar framing allowed per entry on top of the data: one header block,
/// padding to the next block, and room for a long-name or pax record.
const TAR_FRAMING_PER_ENTRY: u64 = 4096;

/// Framing allowed for a tar as a whole: the end marker and the padding that
/// rounds it up to its record size.
const TAR_FRAMING_FIXED: u64 = 1024 * 1024;

/// Counters against the caps of one request.
///
/// The same type serves both passes of an extraction: the first charges what
/// the headers DECLARE, so an honest-looking-but-huge archive is refused before
/// anything is written, and the second charges what is actually WRITTEN, so a
/// header that lies about its size gains nothing. Each pass owns its own budget.
#[derive(Debug)]
pub(crate) struct ExpansionBudget {
    /// Entries charged so far.
    entries: u64,
    /// Bytes charged so far.
    bytes: u64,
    /// The most entries allowed.
    max_entries: u64,
    /// The most bytes allowed.
    max_bytes: u64,
    /// The most bytes the archive's own size lets it expand to, when a ratio
    /// applies. `None` for a creation, which expands nothing.
    ratio_ceiling: Option<u64>,
}

impl ExpansionBudget {
    /// A budget for reading an archive of `archive_length` bytes.
    pub(crate) fn for_extraction(bounds: &ExtractBounds, archive_length: u64) -> Self {
        Self {
            entries: 0,
            bytes: 0,
            max_entries: bounds.max_entries(),
            max_bytes: bounds.max_expanded_bytes(),
            ratio_ceiling: Some(archive_length.saturating_mul(u64::from(bounds.max_ratio()))),
        }
    }

    /// A budget for reading source trees into an archive.
    pub(crate) fn for_creation(bounds: &CreateBounds) -> Self {
        Self {
            entries: 0,
            bytes: 0,
            max_entries: bounds.max_entries(),
            max_bytes: bounds.max_source_bytes(),
            ratio_ceiling: None,
        }
    }

    /// Refuses a count of entries an archive says it holds, before any is read.
    ///
    /// Charges nothing: the entries are charged one by one as they are read, and
    /// this exists so a declared figure far over the cap is refused first.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::EntryLimit`] when `declared` is over the cap.
    pub(crate) fn check_declared_entries(&self, declared: u64) -> Result<(), ArchiveRefusal> {
        if declared > self.max_entries {
            return Err(ArchiveRefusal::EntryLimit);
        }
        Ok(())
    }

    /// Charges one entry.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::EntryLimit`] when it would be one more than allowed.
    pub(crate) fn admit_entry(&mut self) -> Result<(), ArchiveRefusal> {
        if self.entries >= self.max_entries {
            return Err(ArchiveRefusal::EntryLimit);
        }
        self.entries += 1;
        Ok(())
    }

    /// Charges `count` bytes.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::ByteLimit`] when the total passes the byte cap, and
    /// [`ArchiveRefusal::BombRatio`] when it passes the ratio the archive's size
    /// allows (above a floor of one mebibyte).
    pub(crate) fn admit_bytes(&mut self, count: u64) -> Result<(), ArchiveRefusal> {
        self.bytes = self.bytes.saturating_add(count);
        if self.bytes > self.max_bytes {
            return Err(ArchiveRefusal::ByteLimit);
        }
        if let Some(ceiling) = self.ratio_ceiling
            && self.bytes > RATIO_FLOOR
            && self.bytes > ceiling
        {
            return Err(ArchiveRefusal::BombRatio);
        }
        Ok(())
    }

    /// The most decompressed bytes a `.tar.gz` stream may be allowed to deliver:
    /// what the byte cap allows plus tar's own framing.
    ///
    /// Computed from the CAPS and never from a header, so no archive can raise
    /// it. It is the backstop under the per-entry accounting, for the bytes no
    /// entry accounts for (padding, zero blocks after the end marker).
    pub(crate) fn tar_stream_limit(&self) -> u64 {
        self.max_bytes
            .saturating_add(self.max_entries.saturating_mul(TAR_FRAMING_PER_ENTRY))
            .saturating_add(TAR_FRAMING_FIXED)
    }

    /// Bytes charged so far.
    pub(crate) fn bytes(&self) -> u64 {
        self.bytes
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/expansion_budget_tests.rs"]
mod tests;
