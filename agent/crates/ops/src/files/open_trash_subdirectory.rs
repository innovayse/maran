//! Reaching one of the two directories the trash is made of.

use std::ffi::OsStr;
use std::fs::File;

use maran_agent_core::agent_paths::AgentPaths;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_private_directory::open_private_directory;

/// Opens `<home>/.maran-trash/<subdirectory>` from the already-open `home`.
///
/// `subdirectory` is [`AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME`] for trashed
/// entries or [`AgentPaths::TRASH_STAGING_DIRECTORY_NAME`] for copies in
/// progress. Both levels are private directories (see `open_private_directory`),
/// created on first use when `missing` says so and otherwise answered `None` as
/// soon as one of them is absent.
///
/// The trash lives inside the home, on the home's filesystem: a trash move is a
/// rename that cannot leave it, it counts toward the account's quota, and the
/// reserved first path component keeps every other rpc away from it.
///
/// # Errors
///
/// As `open_private_directory` does, for either level.
pub(crate) fn open_trash_subdirectory(
    home: &File,
    subdirectory: &str,
    uid: u32,
    missing: MissingParents,
) -> Result<Option<File>, FilesOpError> {
    let Some(trash) = open_private_directory(
        home,
        OsStr::new(AgentPaths::TRASH_DIRECTORY_NAME),
        uid,
        missing,
    )?
    else {
        return Ok(None);
    };

    open_private_directory(&trash, OsStr::new(subdirectory), uid, missing)
}

#[cfg(test)]
#[path = "../tests/files/open_trash_subdirectory_tests.rs"]
mod tests;
