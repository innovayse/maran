//! Removing the trash entries older than a retention, as the account.

use std::ffi::OsStr;
use std::fs::File;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_home::open_home;
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::remove_trash_entry::remove_trash_entry;

/// Seconds in a day.
const SECONDS_PER_DAY: u64 = 86_400;

/// Longest a copy may sit in the staging area before the purge sweeps it.
///
/// One day: a copy that is running is bounded by the child's patience (ten
/// minutes), so anything older was abandoned by a child that was killed or a
/// host that restarted.
const STAGING_RETENTION_SECONDS: u64 = SECONDS_PER_DAY;

/// Most names read from one directory in one run.
const MAXIMUM_NAMES: usize = 100_000;

/// Removes trash entries trashed more than `older_than_days` days before `now_unix`.
///
/// Age is read from the entry's identifier, which the agent wrote, and the
/// comparison is strict: an entry exactly the retention old is kept. `now_unix`
/// is passed in and never read here, so a test drives time itself and the
/// production caller reads the clock once, in the parent, before the fork. A
/// name that is not an identifier is left alone, except an empty directory,
/// which is removed (it can only be what a crash left). The copy staging area is
/// swept in the same pass: an entry in it older than one day is an abandoned
/// copy and is removed with everything in it.
///
/// Answers how many trash entries were removed; staging sweeps are not counted,
/// because the figure is the one the panel shows as "entries removed".
/// Idempotent, and bounded by 100 000 names per run: a larger trash is finished
/// by the next run.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do, and the errors of `remove_trash_entry`.
pub(crate) fn purge_in_home(
    home: &Path,
    older_than_days: u32,
    now_unix: u64,
    uid: u32,
) -> Result<u64, FilesOpError> {
    let home_directory = open_home(home, uid)?;
    let retention = u64::from(older_than_days).saturating_mul(SECONDS_PER_DAY);

    let mut removed = 0;
    if let Some(entries) = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )? {
        removed = sweep(&entries, retention, now_unix, uid, true)?;
    }
    if let Some(staging) = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )? {
        sweep(&staging, STAGING_RETENTION_SECONDS, now_unix, uid, false)?;
    }

    Ok(removed)
}

/// Removes the identifier-named entries of `directory` older than `retention`.
///
/// `remove_empty_strays` also removes an empty directory whose name is not an
/// identifier; staging has no such rule, so a stray name there is left alone.
/// Both the trash and the staging area are panel-managed space, so an entry
/// holding a read-only directory is reclaimed (`remove_trash_entry` widens it)
/// and the purge always frees the quota. Answers how many it removed.
fn sweep(
    directory: &File,
    retention: u64,
    now_unix: u64,
    uid: u32,
    remove_empty_strays: bool,
) -> Result<u64, FilesOpError> {
    let read = list_directory_names(directory, MAXIMUM_NAMES)
        .map_err(|_| FilesOpError::DirectoryUnusable)?;

    let mut removed: u64 = 0;
    for raw in &read.names {
        if raw == OsStr::new(".") || raw == OsStr::new("..") {
            continue;
        }
        let identifier = raw.to_str().and_then(|name| TrashEntryId::parse(name).ok());
        match identifier {
            Some(id) => {
                if now_unix.saturating_sub(id.unix_seconds()) > retention {
                    remove_trash_entry(directory, raw, uid)?;
                    removed = removed.saturating_add(1);
                }
            }
            None if remove_empty_strays => {
                let is_directory = stat_in_directory(directory, raw)
                    .is_ok_and(|stat| stat.kind == EntryKind::Directory);
                // `rmdir` refuses a directory that holds anything, so this
                // cannot remove more than an empty one.
                if is_directory && remove_directory_in_directory(directory, raw).is_ok() {
                    removed = removed.saturating_add(1);
                }
            }
            None => {}
        }
    }

    Ok(removed)
}

#[cfg(test)]
#[path = "../tests/files/purge_in_home_tests.rs"]
mod tests;
