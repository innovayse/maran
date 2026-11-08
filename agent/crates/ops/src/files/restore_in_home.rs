//! Moving one entry back out of the account's trash, as the account.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::{DIRECTORY_FLAGS, open_home};
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::refuse_taken_name::refuse_taken_name;
use crate::files::rename_failure::rename_failure;

/// Moves trash entry `id` back into the home at `destination`, as the account owning `uid`.
///
/// **The trash is the customer's own data and the customer can edit it**, so
/// nothing in it is trusted: the identifier is a parsed [`TrashEntryId`], the
/// entry must be a directory the account owns with a payload in it, and the
/// destination is a [`MutablePath`], so it cannot be the home, cannot reach the
/// trash and cannot contain `..`. Where the entry used to live is read by
/// `trashed_original_in_home` and validated by the caller before it arrives here
/// as a destination, which is how a forged record is refused: it can at most aim
/// a restore at a place the account could have written to anyway.
///
/// The destination's missing parents are created, a destination that exists is
/// [`FilesOpError::AlreadyExists`] and the entry stays in the trash, and the
/// move is one no-replace rename between two held descriptors, exactly as
/// `move_in_home`. Once it has happened the emptied entry directory and its
/// record are removed, best effort: a leftover is an empty entry the purge
/// collects.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do; [`FilesOpError::NotFound`] when there is no trash or no such
/// entry; [`FilesOpError::InvalidTrashEntry`] when the entry is not in the shape
/// the agent writes; [`FilesOpError::AlreadyExists`] when the destination is
/// taken; and the verdicts of `rename_failure`.
pub(crate) fn restore_in_home(
    home: &Path,
    id: &TrashEntryId,
    destination: &MutablePath,
    uid: u32,
) -> Result<(), FilesOpError> {
    let home_directory = open_home(home, uid)?;
    let entries = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )?
    .ok_or(FilesOpError::NotFound)?;

    let entry_name = OsStr::new(id.as_str());
    let entry = open_entry(&entries, entry_name, uid)?;
    let payload_name = OsStr::new(AgentPaths::TRASH_PAYLOAD_NAME);
    match stat_in_directory(&entry, payload_name) {
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::InvalidTrashEntry);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    }

    let target_parent = open_mutable_parent(home, destination, uid, MissingParents::Create)?;
    let target_name = OsStr::new(destination.file_name());
    refuse_taken_name(&target_parent, target_name)?;

    rename_no_replace_between_directories(&entry, payload_name, &target_parent, target_name)
        .map_err(|error| rename_failure(&error))?;

    let _ = remove_file_in_directory(&entry, OsStr::new(AgentPaths::TRASH_ORIGINAL_NAME));
    let _ = remove_directory_in_directory(&entries, entry_name);

    Ok(())
}

/// Opens the directory of one trash entry and proves it is the account's.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`] when there is no such entry and
/// [`FilesOpError::InvalidTrashEntry`] when the name is held by something that
/// is not a directory the account owns.
fn open_entry(entries: &File, name: &OsStr, uid: u32) -> Result<File, FilesOpError> {
    let stat = match stat_in_directory(entries, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::MutationFailed),
    };
    if stat.kind != EntryKind::Directory || stat.uid != uid {
        return Err(FilesOpError::InvalidTrashEntry);
    }

    open_in_directory(entries, name, DIRECTORY_FLAGS).map_err(|_| FilesOpError::InvalidTrashEntry)
}

#[cfg(test)]
#[path = "../tests/files/restore_in_home_tests.rs"]
mod tests;
