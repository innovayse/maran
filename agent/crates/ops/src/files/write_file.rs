//! WriteFile: one file into a customer's home, as the customer.

use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;
use crate::files::{FilesHost, FilesOpError};

/// Streams `source` into a new file at the input's path and reports how many bytes landed.
///
/// Two callers: the ACME HTTP-01 challenge (a token file under a site's document root that a
/// certificate authority fetches once, replaced when re-issued, and removed again by
/// [`super::delete_entry`]) and the file manager's upload (up to the 256 MiB ceiling, refusing
/// to replace). Both are inside a customer's home, so the file is written by a process that
/// has dropped to that account, never by the root daemon (rules/security.md item 3).
///
/// First, as root and before anything is created, the free-space floor: the filesystem holding
/// the account homes must still have `floor` available to an unprivileged writer (issue #148).
///
/// Then ONE privileged step, as the account: create the missing directories, stream the body
/// into a temporary file beside the target, and publish it by rename. The body is never held
/// in the root daemon's memory and never published unless the source reported itself complete
/// (see [`UploadSource`]): an upload whose sender went away leaves the destination as it was.
///
/// **The descriptor walk IS the containment, and there is deliberately no second check.** The
/// walk starts at `/home/<account>`, follows no symlink at any level, and traverses a component
/// list that provably holds no `..`, no `/` and no empty component; a descent with those
/// properties cannot end outside the home. A defensive containment call here could not fail,
/// and a defensive call that cannot fail is decoration a later reader mistakes for protection.
///
/// With `fail_if_exists` unset the write is idempotent as `files.proto` requires: writing the
/// same content twice leaves the same end state, and different content replaces it.
///
/// # Errors
///
/// Returns [`FilesOpError::FreeSpace`] when the home filesystem is below the floor or cannot
/// be measured, before anything is created; [`FilesOpError::Privilege`] when the account
/// cannot be resolved or the privileged work fails; and, from the child,
/// [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::AlreadyExists`], [`FilesOpError::LimitReached`],
/// [`FilesOpError::QuotaExceeded`] and [`FilesOpError::WriteFailed`] as `write_in_home`
/// documents them.
pub fn write_file(
    host: &dyn FilesHost,
    input: &WriteFileInput,
    source: &mut dyn UploadSource,
    floor: &FreeSpaceFloor,
) -> Result<u64, FilesOpError> {
    floor.admit(host.available_bytes(Path::new(AgentPaths::ACCOUNT_HOME_ROOT)))?;

    host.write_as_account(input, source)
}

#[cfg(test)]
#[path = "../tests/files/write_file_tests.rs"]
mod tests;
