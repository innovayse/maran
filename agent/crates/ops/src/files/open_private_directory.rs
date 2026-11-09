//! Opening, and if asked creating, a directory only the account may enter.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io;
use std::os::unix::fs::{MetadataExt, PermissionsExt};

use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::DIRECTORY_FLAGS;

/// Permission bits of a private directory, set explicitly on its descriptor.
const PRIVATE_MODE: u32 = 0o700;

/// Opens the directory `name` inside `parent`, which only the account may enter.
///
/// What the trash and its staging area are made of. Unlike the levels of a
/// walk (`open_level`, which widens a created directory to `0o755` so a web
/// server can traverse it) these are never served, so a directory this CREATES
/// is `0o700`, applied with an explicit `fchmod` on the descriptor so the
/// daemon's umask cannot narrow it into something unusable.
///
/// With [`MissingParents::Create`] the answer is always `Some`. With
/// [`MissingParents::Require`] a directory that is not there is `None`, which is
/// how a listing of a trash that was never used reads as empty instead of as a
/// failure; a symlink, a plain file or a directory owned by somebody else at the
/// name is a refusal, never `None`.
///
/// # Errors
///
/// Returns [`FilesOpError::DirectoryUnusable`] when the directory cannot be
/// created or opened, is a symlink or not a directory, or is not owned by the
/// account.
pub(crate) fn open_private_directory(
    parent: &File,
    name: &OsStr,
    uid: u32,
    missing: MissingParents,
) -> Result<Option<File>, FilesOpError> {
    let mut created = false;
    if missing == MissingParents::Create {
        match make_directory_in_directory(parent, name, PRIVATE_MODE) {
            Ok(()) => created = true,
            // Already there is the ordinary case, and is not trusted to be a
            // directory: the open below decides that.
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
            Err(_) => return Err(FilesOpError::DirectoryUnusable),
        }
    }

    let opened = match open_in_directory(parent, name, DIRECTORY_FLAGS) {
        Ok(opened) => opened,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return if missing == MissingParents::Require {
                Ok(None)
            } else {
                Err(FilesOpError::DirectoryUnusable)
            };
        }
        Err(_) => return Err(FilesOpError::DirectoryUnusable),
    };

    let metadata = opened
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(FilesOpError::DirectoryUnusable);
    }

    if created {
        opened
            .set_permissions(Permissions::from_mode(PRIVATE_MODE))
            .map_err(|_| FilesOpError::DirectoryUnusable)?;
    }

    Ok(Some(opened))
}

#[cfg(test)]
#[path = "../tests/files/open_private_directory_tests.rs"]
mod tests;
