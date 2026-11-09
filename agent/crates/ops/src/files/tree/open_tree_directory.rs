//! Descending into one directory of a tree being walked.

use std::ffi::OsStr;
use std::fs::File;
use std::os::unix::fs::MetadataExt;

use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::files::FilesOpError;
use crate::files::open_home::DIRECTORY_FLAGS;

/// Opens the directory `name` inside `parent`, as a level of a tree walk.
///
/// `O_NOFOLLOW | O_DIRECTORY` from the descriptor already held, so a name
/// swapped for a link after it was judged is refused instead of followed, and
/// two further conditions that make a recursion safe to continue: the directory
/// must be owned by the account (the claim that survives write access being
/// handed around) and must be on `device`, the filesystem the walk began on, so
/// a filesystem mounted inside the tree cannot widen the operation.
///
/// # Errors
///
/// Returns [`FilesOpError::DirectoryUnusable`] when the open is refused, which
/// covers a symlink, a non-directory and a directory the account cannot read;
/// [`FilesOpError::NotOwned`] when it is another owner's; and
/// [`FilesOpError::CrossesDevice`] when it is on another filesystem.
pub(crate) fn open_tree_directory(
    parent: &File,
    name: &OsStr,
    uid: u32,
    device: u64,
) -> Result<File, FilesOpError> {
    let opened = open_in_directory(parent, name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::DirectoryUnusable)?;

    let metadata = opened
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
    if !metadata.is_dir() {
        return Err(FilesOpError::DirectoryUnusable);
    }
    if metadata.uid() != uid {
        return Err(FilesOpError::NotOwned);
    }
    if metadata.dev() != device {
        return Err(FilesOpError::CrossesDevice);
    }

    Ok(opened)
}

#[cfg(test)]
#[path = "../../tests/files/tree/open_tree_directory_tests.rs"]
mod tests;
