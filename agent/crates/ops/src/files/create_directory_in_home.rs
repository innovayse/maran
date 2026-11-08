//! Making one directory in a customer's home, as the customer.

use std::ffi::OsStr;
use std::fs::Permissions;
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::path::Path;

use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;
use crate::files::io_failure::io_failure;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::DIRECTORY_FLAGS;
use crate::files::open_mutable_parent::open_mutable_parent;

/// Permission bits the directory is created with, before its own mode is set.
///
/// Narrow on purpose, as in `open_level`: the directory is widened to the
/// requested mode on its own descriptor a moment later, so it is never, at any
/// instant, enterable by anybody but the account, and a crash in between leaves
/// a private directory.
const CREATE_MODE: libc::mode_t = 0o700;

/// Creates the directory `path` inside `home` with permission bits `mode`, as
/// the account owning `uid`.
///
/// Missing parents are created by the descent (`open_mutable_parent` with
/// [`MissingParents::Create`]) with the agent's directory mode, and parents that
/// exist are left exactly as they are. The final directory is made with
/// `mkdirat`, which never follows a symlink and refuses a name that is taken,
/// so a file or a link already at the name is [`FilesOpError::AlreadyExists`]
/// and nothing is replaced; it is then opened `O_NOFOLLOW | O_DIRECTORY` and
/// given `mode` with an `fchmod` on that descriptor, not on a name and not left
/// to the daemon's umask.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as `open_mutable_parent` does; [`FilesOpError::AlreadyExists`] when the final
/// name is taken; [`FilesOpError::QuotaExceeded`] when the account's quota ends
/// the creation; and [`FilesOpError::MutationFailed`] when the directory cannot
/// be created, reopened or given its mode.
pub(crate) fn create_directory_in_home(
    home: &Path,
    path: &MutablePath,
    mode: FileMode,
    uid: u32,
) -> Result<(), FilesOpError> {
    let parent = open_mutable_parent(home, path, uid, MissingParents::Create)?;
    let name = OsStr::new(path.file_name());

    match make_directory_in_directory(&parent, name, CREATE_MODE) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {
            return Err(FilesOpError::AlreadyExists);
        }
        Err(error) => return Err(io_failure(&error, FilesOpError::MutationFailed)),
    }

    let created = open_in_directory(&parent, name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::MutationFailed)?;
    // Only a directory this call just made is chmodded. If the name was swapped
    // for another directory in the instant since, it is still one the account
    // owns (checked) and could have changed itself.
    let metadata = created
        .metadata()
        .map_err(|_| FilesOpError::MutationFailed)?;
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(FilesOpError::MutationFailed);
    }

    created
        .set_permissions(Permissions::from_mode(mode.bits()))
        .map_err(|_| FilesOpError::MutationFailed)
}

#[cfg(test)]
#[path = "../tests/files/create_directory_in_home_tests.rs"]
mod tests;
