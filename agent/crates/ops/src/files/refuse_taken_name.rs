//! Refusing a destination name that something already holds.

use std::ffi::OsStr;
use std::fs::File;
use std::io;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;

/// Succeeds only when nothing at all is at `name` inside `parent`.
///
/// An `fstatat` with no link followed, so a symlink at the name (dangling or
/// not) counts as taken. It gives the typed answer early; the rename that
/// follows refuses the same name with `EEXIST` for the instant between the two,
/// and that kernel refusal, not this check, is what guarantees nothing is
/// replaced.
///
/// # Errors
///
/// Returns [`FilesOpError::UnsupportedEntry`] when the name is held by a symlink,
/// [`FilesOpError::AlreadyExists`] when it is held by anything else, and
/// [`FilesOpError::MutationFailed`] when it cannot be read.
pub(crate) fn refuse_taken_name(parent: &File, name: &OsStr) -> Result<(), FilesOpError> {
    match stat_in_directory(parent, name) {
        Ok(stat) if stat.kind == EntryKind::Symlink => Err(FilesOpError::UnsupportedEntry),
        Ok(_) => Err(FilesOpError::AlreadyExists),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(_) => Err(FilesOpError::MutationFailed),
    }
}

#[cfg(test)]
#[path = "../tests/files/refuse_taken_name_tests.rs"]
mod tests;
