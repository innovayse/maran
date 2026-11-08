//! ExtractArchive: an archive's members into a customer's home, as the customer.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::extract_archive_input::ExtractArchiveInput;
use crate::files::{ArchiveHost, FilesOpError};

/// Extracts the archive `input` names and reports what was written.
///
/// First, as root and before anything is created, the free-space floor: the
/// filesystem holding the account homes must still have `floor` available to an
/// unprivileged writer (issue #148). Then the whole extraction in one forked
/// child that has dropped to the account: the archive is opened, judged as a
/// whole, and only then written, member by member, through descriptors
/// (`extract_in_home` documents each step). The parser never runs as root.
///
/// Refuses, as a whole and before writing, an archive holding a link, a device,
/// a FIFO, a sparse member, a name that is absolute or leaves the destination, an
/// encrypted member, a compression method other than stored or deflate, two
/// members of one name, or an expansion past the caps the request stated. Never
/// overwrites: a member whose name is taken is `AlreadyExists` and the extraction
/// is rolled back to what it was; an existing directory is merged into.
///
/// **Cancellation class: run to completion.** The extraction is one child that
/// either finishes or removes what it made, so a caller that stops listening
/// changes nothing; there is no half-made state to reclaim afterwards. (A child
/// killed by the agent's patience, two minutes, cannot clean up and may leave
/// the files it had written; they belong to the account.)
///
/// Not idempotent in the sense of "a second run changes nothing by succeeding":
/// the second run finds the first run's files and is refused with
/// `AlreadyExists`, again changing nothing.
///
/// # Errors
///
/// Returns [`FilesOpError::FreeSpace`] below the floor, [`FilesOpError::Saturated`]
/// when too many archive operations are running, [`FilesOpError::Privilege`] when
/// the account cannot be resolved or the drop fails, [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`], [`FilesOpError::NotFound`] and
/// [`FilesOpError::NotARegularFile`] for the archive and the walk,
/// [`FilesOpError::ArchiveUnsafe`] and [`FilesOpError::ArchiveUnreadable`] for the
/// archive itself, [`FilesOpError::AlreadyExists`] for a taken name,
/// [`FilesOpError::LimitReached`] for the entry or byte cap,
/// [`FilesOpError::QuotaExceeded`] and [`FilesOpError::WriteFailed`] for the disk.
pub fn extract_archive(
    host: &dyn ArchiveHost,
    input: &ExtractArchiveInput,
    floor: &FreeSpaceFloor,
) -> Result<ArchiveReport, FilesOpError> {
    floor.admit(host.available_bytes(Path::new(AgentPaths::ACCOUNT_HOME_ROOT)))?;

    host.extract_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/extract_archive_tests.rs"]
mod tests;
