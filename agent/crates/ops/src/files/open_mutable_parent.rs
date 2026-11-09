//! Walking to the directory that holds a mutable path's final name.

use std::ffi::OsStr;
use std::fs::File;
use std::path::Path;

use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::open_home;
use crate::files::open_level::open_level;

/// Opens the directory holding `path`'s final name, descending from `home`.
///
/// The twin of `open_parent_directory` for the deeper, home-free paths of the
/// file manager, built from the same two steps (`open_home`, `open_level`) so the
/// containment argument is made once: the home is opened by name, the one name a
/// customer cannot rewrite, and every component below it is reached from the
/// descriptor above with `openat` and `O_NOFOLLOW`, each required to be a
/// directory the account owns. A path with no parents answers the home itself.
///
/// `missing` decides what happens to a level that is not there; a move, a copy
/// and a restore create their destination's parents, and everything that
/// addresses an existing entry requires them.
///
/// Runs inside a forked child that has dropped to the account.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] when the home is missing, is not a
/// directory or is not the account's, and [`FilesOpError::DirectoryUnusable`]
/// when a level is missing (and not created), is not a directory, is a symlink
/// or is not the account's.
pub(crate) fn open_mutable_parent(
    home: &Path,
    path: &MutablePath,
    uid: u32,
    missing: MissingParents,
) -> Result<File, FilesOpError> {
    let mut directory = open_home(home, uid)?;

    for component in path.parent_components() {
        directory = open_level(&directory, OsStr::new(component.as_str()), uid, missing)?;
    }

    Ok(directory)
}

#[cfg(test)]
#[path = "../tests/files/open_mutable_parent_tests.rs"]
mod tests;
