//! Walking to the directory a whole browse path names.

use std::ffi::OsStr;
use std::fs::File;
use std::path::Path;

use maran_agent_core::validation::fs::browse_path::BrowsePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::open_home;
use crate::files::open_level::open_level;

/// Opens the directory `path` names inside `home`, descending one descriptor at a time.
///
/// The read-side twin of `open_parent_directory`, built from the same two steps
/// (`open_home`, `open_level`) so the containment argument is made once: the
/// home is opened by name, the one name a customer cannot rewrite, and every
/// component below it is reached from the descriptor above with `openat` and
/// `O_NOFOLLOW`, each required to be a directory the account owns. The home
/// itself is the answer for an empty path.
///
/// Levels are only ever REQUIRED ([`MissingParents::Require`]): a browse creates
/// nothing.
///
/// Runs inside a forked child that has dropped to the account; the dropped uid
/// and the walk answer different attacks and neither replaces the other.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] when the home is missing, is not a
/// directory or is not the account's, and [`FilesOpError::DirectoryUnusable`]
/// when a level is missing, is not a directory, is a symlink or is not the
/// account's.
pub(crate) fn open_directory_in_home(
    home: &Path,
    path: &BrowsePath,
    uid: u32,
) -> Result<File, FilesOpError> {
    let mut directory = open_home(home, uid)?;

    for component in path.components() {
        directory = open_level(
            &directory,
            OsStr::new(component.as_str()),
            uid,
            MissingParents::Require,
        )?;
    }

    Ok(directory)
}

#[cfg(test)]
#[path = "../tests/files/open_directory_in_home_tests.rs"]
mod tests;
