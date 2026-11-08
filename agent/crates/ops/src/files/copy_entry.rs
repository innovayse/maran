//! CopyEntry: a file or a tree, copied inside a customer's home.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use crate::files::model::copy_entry_input::CopyEntryInput;
use crate::files::model::copy_report::CopyReport;
use crate::files::{FilesHost, FilesOpError};

/// Copies one file or directory tree inside an account's home, as the account.
///
/// First, as root and before anything is created, the free-space floor: the
/// filesystem holding the account homes must still have `floor` available to an
/// unprivileged writer (issue #148), so a copy cannot drive the disk to the point
/// where every other writer on it fails too.
///
/// Then the copy, all or nothing: the tree is measured and refused as a whole
/// when it passes the stated entries or bytes
/// ([`FilesOpError::LimitReached`]), is deeper than the ceiling
/// ([`FilesOpError::TooDeep`]), or holds anything but regular files and
/// directories ([`FilesOpError::UnsupportedEntry`]); it is written into a staging
/// directory and renamed into place without replacing, so a failed or stopped
/// copy leaves nothing at the destination. A destination that exists is
/// [`FilesOpError::AlreadyExists`], a missing source is
/// [`FilesOpError::NotFound`], a destination inside the source is
/// [`FilesOpError::IntoItself`], and the account's quota ending the copy is
/// [`FilesOpError::QuotaExceeded`]. Permission bits are kept; owner and times
/// are not.
///
/// # Errors
///
/// Returns [`FilesOpError::FreeSpace`] when the home filesystem is below the
/// floor or cannot be measured, before anything is created;
/// [`FilesOpError::Saturated`] at the bulk bound; and the verdicts above, plus
/// [`FilesOpError::Privilege`], [`FilesOpError::HomeUnusable`],
/// [`FilesOpError::DirectoryUnusable`], [`FilesOpError::NotOwned`],
/// [`FilesOpError::CrossesDevice`], [`FilesOpError::WriteFailed`] and
/// [`FilesOpError::MutationFailed`].
pub fn copy_entry(
    host: &dyn FilesHost,
    input: &CopyEntryInput,
    floor: &FreeSpaceFloor,
) -> Result<CopyReport, FilesOpError> {
    floor.admit(host.available_bytes(Path::new(AgentPaths::ACCOUNT_HOME_ROOT)))?;

    host.copy_as_account(input)
}

#[cfg(test)]
#[path = "../tests/files/copy_entry_tests.rs"]
mod tests;
