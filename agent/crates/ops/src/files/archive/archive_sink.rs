//! Somewhere an archive is written to, whatever its format.

use std::fs::File;
use std::io::Read;

use maran_agent_core::validation::fs::archive_member_name::ArchiveMemberName;

use super::archive_refusal::ArchiveRefusal;

/// The writing half of an archive format.
///
/// Two implementations, one per format, and a trait because creation does the
/// same walk for both and only the framing differs. Names arrive as
/// [`ArchiveMemberName`], so a name the format could be made to write as
/// something else (`..`, a leading `/`) cannot reach it. Modes are chosen by the
/// sink, never taken from the source tree: files 0644 or 0755 and directories
/// 0755, the same three values an extraction produces.
pub(crate) trait ArchiveSink {
    /// Adds a directory entry.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::Quota`] or [`ArchiveRefusal::WriteFailed`].
    fn add_directory(&mut self, name: &ArchiveMemberName) -> Result<(), ArchiveRefusal>;

    /// Adds a file of exactly `size` bytes read from `data`.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::ReadFailed`] when `data` fails or ends short, and
    /// [`ArchiveRefusal::Quota`] or [`ArchiveRefusal::WriteFailed`] when the
    /// archive cannot be written.
    fn add_file(
        &mut self,
        name: &ArchiveMemberName,
        executable: bool,
        size: u64,
        data: &mut dyn Read,
    ) -> Result<(), ArchiveRefusal>;

    /// Writes whatever closes the archive and hands back the finished file.
    ///
    /// # Errors
    ///
    /// [`ArchiveRefusal::Quota`] or [`ArchiveRefusal::WriteFailed`].
    fn finish(self: Box<Self>) -> Result<File, ArchiveRefusal>;
}
