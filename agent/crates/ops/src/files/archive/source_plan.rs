//! What a creation found when it measured its sources.

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;

use super::archive_refusal::ArchiveRefusal;
use super::planned_entry::PlannedEntry;

/// Every entry a creation will write, in the order it will write them, and where
/// their archive names begin.
///
/// A directory always precedes what it holds. The archive name of an entry is
/// its path with the sources' common parent taken off, so the archive does not
/// carry the account's directory layout above what the customer selected.
#[derive(Debug)]
pub(crate) struct SourcePlan {
    /// The entries to write.
    pub(crate) entries: Vec<PlannedEntry>,
    /// Bytes of a path to skip to reach its archive name.
    pub(crate) strip: usize,
}

impl SourcePlan {
    /// The archive name of `entry`.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::TooDeep`] for a name with too many components and
    /// [`ArchiveRefusal::UnsupportedEntry`] for any other name an archive cannot
    /// hold (a backslash, a control character, bytes that are not UTF-8).
    pub(crate) fn name_of(
        &self,
        entry: &PlannedEntry,
    ) -> Result<ArchiveMemberName, ArchiveRefusal> {
        use maran_agent_core::validation::fs::archive_member_name_error::ArchiveMemberNameError;

        let relative = entry.path.get(self.strip..).unwrap_or("");
        ArchiveMemberName::parse(relative.as_bytes()).map_err(|error| match error {
            ArchiveMemberNameError::TooDeep => ArchiveRefusal::TooDeep,
            _ => ArchiveRefusal::UnsupportedEntry,
        })
    }
}
