//! CreateArchive: files of a customer's home into one archive, as the customer.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use crate::files::model::archive_report::ArchiveReport;
use crate::files::model::create_archive_input::CreateArchiveInput;
use crate::files::{ArchiveHost, FilesOpError};

/// Creates the archive `input` describes and reports its entries and size.
///
/// First, as root, the free-space floor, as [`super::extract_archive`] does.
/// Then one forked child that has dropped to the account measures the sources,
/// writes the archive to a temporary file beside its destination, and publishes
/// it with a rename that cannot replace (`create_in_home` documents each step).
///
/// Refuses the WHOLE request, before writing, for a symbolic link, FIFO, socket
/// or device anywhere in a source, and for a name an archive cannot hold: an
/// archive that silently omits what was selected is worse than none. A
/// destination inside a source is refused earlier, by the input type. A
/// destination that exists is `AlreadyExists` and is never replaced.
///
/// **Cancellation class: run to completion.** The archive is complete at its
/// destination or absent, so a caller that stops listening changes nothing. (A
/// child killed by the agent's patience, two minutes, may leave a hidden
/// `.maran-archive-*` temporary the account owns.)
///
/// # Errors
///
/// Returns [`FilesOpError::FreeSpace`], [`FilesOpError::Saturated`],
/// [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`] and [`FilesOpError::NotFound`] as
/// [`super::extract_archive`] does; [`FilesOpError::UnsupportedEntry`] for a link
/// or special file or an unrepresentable name in a source;
/// [`FilesOpError::TooDeep`] for a tree deeper than a name may be;
/// [`FilesOpError::AlreadyExists`] when the destination is taken;
/// [`FilesOpError::LimitReached`] for the entry or byte cap;
/// [`FilesOpError::ReadFailed`], [`FilesOpError::QuotaExceeded`] and
/// [`FilesOpError::WriteFailed`] for the disk.
pub fn create_archive(
    host: &dyn ArchiveHost,
    input: &CreateArchiveInput,
    floor: &FreeSpaceFloor,
) -> Result<ArchiveReport, FilesOpError> {
    floor.admit(host.available_bytes(Path::new(AgentPaths::ACCOUNT_HOME_ROOT)))?;

    host.create_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/create_archive_tests.rs"]
mod tests;
