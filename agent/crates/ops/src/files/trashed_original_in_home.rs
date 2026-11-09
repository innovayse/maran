//! Reading where one trash entry came from, as the account.

use std::ffi::OsStr;
use std::io;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::{DIRECTORY_FLAGS, open_home};
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::read_trash_original::read_trash_original;

/// Reads the recorded original path of trash entry `id`, as the account owning `uid`.
///
/// Returns the text as found. It is the customer's to edit, so the caller parses
/// it as a `MutablePath` before it names anything (`parse_trashed_original`),
/// which is where a forged record is refused. Read in its own short fork rather
/// than inside the restore so the panel can be told the path the entry now has,
/// which the restore's two-integer report cannot carry.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do, [`FilesOpError::NotFound`] when there is no trash or no such
/// entry, and [`FilesOpError::InvalidTrashEntry`] when the entry is not a
/// directory the account owns or its record is missing or unreadable.
pub(crate) fn trashed_original_in_home(
    home: &Path,
    id: &TrashEntryId,
    uid: u32,
) -> Result<String, FilesOpError> {
    let home_directory = open_home(home, uid)?;
    let entries = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )?
    .ok_or(FilesOpError::NotFound)?;

    let name = OsStr::new(id.as_str());
    let stat = match stat_in_directory(&entries, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    };
    if stat.kind != EntryKind::Directory || stat.uid != uid {
        return Err(FilesOpError::InvalidTrashEntry);
    }
    let entry = open_in_directory(&entries, name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::InvalidTrashEntry)?;

    read_trash_original(&entry, uid)
}

#[cfg(test)]
#[path = "../tests/files/trashed_original_in_home_tests.rs"]
mod tests;
