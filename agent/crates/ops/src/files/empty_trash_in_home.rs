//! Removing everything in the account's trash, as the account.

use std::ffi::OsStr;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::list_directory_names::list_directory_names;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::open_home;
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::remove_trash_entry::remove_trash_entry;

/// Names read from the trash per pass.
const NAMES_PER_PASS: usize = 4096;

/// Removes every entry of the trash of the account owning `uid` and answers how many.
///
/// "Everything" means whatever is in the entries directory, whatever shape it
/// has: this is the customer asking for their own trash to be gone, and a name
/// that is not in the shape the agent writes is still theirs. Each is removed by
/// `remove_trash_entry`, so no link inside is followed and a directory with a
/// read-only mode is widened first: emptying the trash always frees the quota. The copy staging area is
/// not touched; the purge sweeps that.
///
/// An empty or never-used trash is success with zero. The count is of top-level
/// entries removed. Idempotent: a run that was stopped is finished by running it
/// again, because every pass re-reads what is left.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do, and the errors of `remove_trash_entry`.
pub(crate) fn empty_trash_in_home(home: &Path, uid: u32) -> Result<u64, FilesOpError> {
    let home_directory = open_home(home, uid)?;
    let Some(entries) = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )?
    else {
        return Ok(0);
    };

    let mut removed: u64 = 0;
    loop {
        let read = list_directory_names(&entries, NAMES_PER_PASS)
            .map_err(|_| FilesOpError::DirectoryUnusable)?;

        let mut any = false;
        for name in &read.names {
            if name == OsStr::new(".") || name == OsStr::new("..") {
                continue;
            }
            any = true;
            remove_trash_entry(&entries, name, uid)?;
            removed = removed.saturating_add(1);
        }
        if !any {
            return Ok(removed);
        }
    }
}

#[cfg(test)]
#[path = "../tests/files/empty_trash_in_home_tests.rs"]
mod tests;
