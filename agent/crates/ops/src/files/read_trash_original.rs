//! Reading the record of where a trashed entry came from.

use std::ffi::OsStr;
use std::fs::File;
use std::io::Read;
use std::os::unix::fs::MetadataExt;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::files::FilesOpError;
use crate::files::entry_open_flags::ENTRY_OPEN_FLAGS;

/// Longest original path the trash records (`PATH_MAX`), and so the most this reads.
pub(crate) const MAXIMUM_ORIGINAL_BYTES: u64 = 4096;

/// Reads `original` out of one trash entry's directory.
///
/// The file is the customer's to edit, so nothing about it is trusted: it is
/// opened without following a link and without blocking, must be a regular file
/// the account owns, and is read through a hard ceiling. The text is returned as
/// found; a restore parses it as a mutable path before using it, and a listing
/// only shows it.
///
/// # Errors
///
/// Returns [`FilesOpError::InvalidTrashEntry`] when the file is missing, is not
/// a regular file owned by the account, is longer than 4096 bytes or is not
/// UTF-8.
pub(crate) fn read_trash_original(entry: &File, uid: u32) -> Result<String, FilesOpError> {
    let mut file = open_in_directory(
        entry,
        OsStr::new(AgentPaths::TRASH_ORIGINAL_NAME),
        ENTRY_OPEN_FLAGS,
    )
    .map_err(|_| FilesOpError::InvalidTrashEntry)?;

    let metadata = file
        .metadata()
        .map_err(|_| FilesOpError::InvalidTrashEntry)?;
    if !metadata.is_file() || metadata.uid() != uid || metadata.len() > MAXIMUM_ORIGINAL_BYTES {
        return Err(FilesOpError::InvalidTrashEntry);
    }

    let mut text = String::new();
    file.by_ref()
        .take(MAXIMUM_ORIGINAL_BYTES)
        .read_to_string(&mut text)
        .map_err(|_| FilesOpError::InvalidTrashEntry)?;

    Ok(text)
}

#[cfg(test)]
#[path = "../tests/files/read_trash_original_tests.rs"]
mod tests;
