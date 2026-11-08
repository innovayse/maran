//! Deciding whether an entry may be moved or trashed.

use std::ffi::OsStr;
use std::fs::File;
use std::io;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;

/// Judges the entry `name` inside `parent` as the source of a move or a trash.
///
/// One `fstatat` with no link followed, shared by `move_in_home` and
/// `trash_in_home` because a trash is a move and refuses exactly what a move
/// refuses. The entry is not opened, so a FIFO costs nothing.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`] when the entry is not there (also what a
/// retry of a move that already happened sees),
/// [`FilesOpError::UnsupportedEntry`] when it is a symlink,
/// [`FilesOpError::NotOwned`] when the account does not own it, and
/// [`FilesOpError::MutationFailed`] when it cannot be read for another reason.
pub(crate) fn judge_movable_source(
    parent: &File,
    name: &OsStr,
    uid: u32,
) -> Result<(), FilesOpError> {
    let stat = match stat_in_directory(parent, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    };
    if stat.kind == EntryKind::Symlink {
        return Err(FilesOpError::UnsupportedEntry);
    }
    if stat.uid != uid {
        return Err(FilesOpError::NotOwned);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/files/judge_movable_source_tests.rs"]
mod tests;
