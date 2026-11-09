//! Moving one entry into the account's trash, as the account.

use std::ffi::OsStr;
use std::fs::File;
use std::io::Write;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::privs::make_directory_in_directory::make_directory_in_directory;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::validation::fs::mutable_path::MutablePath;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::io_failure::io_failure;
use crate::files::judge_movable_source::judge_movable_source;
use crate::files::model::missing_parents::MissingParents;
use crate::files::mutable_path_components::mutable_path_components;
use crate::files::open_home::{DIRECTORY_FLAGS, open_home};
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::rename_failure::rename_failure;

/// Permission bits of the trash entry's directory and of its record.
const PRIVATE_DIRECTORY_MODE: libc::mode_t = 0o700;

/// Permission bits of the record of the original path.
const RECORD_MODE: libc::mode_t = 0o600;

/// Moves `path` into `<home>/.maran-trash/entries/<id>/payload`, as the account owning `uid`.
///
/// The order is crash-safe and each step is undoable by the next: create the
/// entry's directory (`mkdirat`, which refuses an identifier that exists), write
/// `original` (the path the entry came from, for a restore to default to), then
/// `renameat2(RENAME_NOREPLACE)` the source into `payload`. A crash before the
/// last step leaves an entry with no payload, which the listing leaves out and
/// the purge removes; there is no moment at which the source is gone and the
/// record of where it was is not.
///
/// The trash is inside the home, so the move is an O(1) rename that cannot leave
/// the filesystem the home is on (a source on a mount inside the home is
/// [`FilesOpError::CrossesDevice`]) and the entry keeps counting toward the
/// account's quota. A failure after the entry's directory was made removes
/// what was made, best effort: the failure already worth reporting is not
/// replaced by "cleanup failed".
///
/// The source is judged by `judge_movable_source`: absent, a symlink and an
/// entry the account does not own are refused as a move refuses them. The
/// reserved trash name is refused by `MutablePath` before this runs.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`], [`FilesOpError::DirectoryUnusable`],
/// [`FilesOpError::NotFound`], [`FilesOpError::UnsupportedEntry`] and
/// [`FilesOpError::NotOwned`] as above; [`FilesOpError::QuotaExceeded`] and
/// [`FilesOpError::MutationFailed`] when the entry's directory or record cannot
/// be made; and the verdicts of `rename_failure` for the final rename.
pub(crate) fn trash_in_home(
    home: &Path,
    path: &MutablePath,
    id: &TrashEntryId,
    uid: u32,
) -> Result<(), FilesOpError> {
    let source_parent = open_mutable_parent(home, path, uid, MissingParents::Require)?;
    let source_name = OsStr::new(path.file_name());
    judge_movable_source(&source_parent, source_name, uid)?;

    let home_directory = open_home(home, uid)?;
    let entries = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Create,
    )?
    .ok_or(FilesOpError::DirectoryUnusable)?;

    let entry_name = OsStr::new(id.as_str());
    make_directory_in_directory(&entries, entry_name, PRIVATE_DIRECTORY_MODE)
        .map_err(|error| io_failure(&error, FilesOpError::MutationFailed))?;

    let finished = record_and_move(&entries, entry_name, &source_parent, source_name, path);
    if finished.is_err() {
        remove_entry_directory(&entries, entry_name);
    }

    finished
}

/// Writes the record of the original path, then renames the source into place.
fn record_and_move(
    entries: &File,
    entry_name: &OsStr,
    source_parent: &File,
    source_name: &OsStr,
    path: &MutablePath,
) -> Result<(), FilesOpError> {
    let entry = open_in_directory(entries, entry_name, DIRECTORY_FLAGS)
        .map_err(|_| FilesOpError::MutationFailed)?;

    let mut record = create_file_in_directory(
        &entry,
        OsStr::new(AgentPaths::TRASH_ORIGINAL_NAME),
        RECORD_MODE,
    )
    .map_err(|error| io_failure(&error, FilesOpError::MutationFailed))?;
    record
        .write_all(mutable_path_components(path).join("/").as_bytes())
        .map_err(|error| io_failure(&error, FilesOpError::MutationFailed))?;

    rename_no_replace_between_directories(
        source_parent,
        source_name,
        &entry,
        OsStr::new(AgentPaths::TRASH_PAYLOAD_NAME),
    )
    .map_err(|error| rename_failure(&error))
}

/// Takes away an entry directory this call made and the record inside it.
///
/// Best effort and deliberately silent: it runs on a path that is already
/// reporting a failure, and it only removes names this call created.
fn remove_entry_directory(entries: &File, entry_name: &OsStr) {
    if let Ok(entry) = open_in_directory(entries, entry_name, DIRECTORY_FLAGS) {
        let _ = remove_file_in_directory(&entry, OsStr::new(AgentPaths::TRASH_ORIGINAL_NAME));
    }
    let _ = remove_directory_in_directory(entries, entry_name);
}

#[cfg(test)]
#[path = "../tests/files/trash_in_home_tests.rs"]
mod tests;
