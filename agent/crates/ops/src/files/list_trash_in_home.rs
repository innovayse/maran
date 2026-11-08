//! Reading the account's trash, as the account.

use std::ffi::OsStr;
use std::path::Path;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::model::trashed_entry::TrashedEntry;
use crate::files::open_home::{DIRECTORY_FLAGS, open_home};
use crate::files::open_trash_subdirectory::open_trash_subdirectory;
use crate::files::read_trash_original::read_trash_original;

/// Most entries a listing reports (the newest ones).
pub(crate) const MAXIMUM_LISTED_TRASH_ENTRIES: usize = 10_000;

/// Most names one listing reads from the trash before refusing it as too large.
const MAXIMUM_TRASH_NAMES: usize = 100_000;

/// Lists the trash entries of the account owning `uid`, newest first.
///
/// Nothing in the trash directory is trusted, because the customer can edit
/// their own trash. A name that does not parse as a [`TrashEntryId`] is left
/// out, as is anything that is not a directory the account owns and anything
/// without a payload (an entry a crash left half made); the original path is
/// shown as found when it is readable text and as empty otherwise. The entry's
/// kind and size come from an `fstatat` of the payload with no link followed;
/// the payload is never opened, so a FIFO costs nothing, and a directory is not
/// totalled (size zero) to keep a listing cheap.
///
/// A trash that was never used is an empty listing and not a failure. A trash
/// with more than 100 000 names is [`FilesOpError::TooLarge`]; one with more
/// than 10 000 valid entries reports the newest 10 000.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walks do, [`FilesOpError::TooLarge`] as above, and
/// [`FilesOpError::DirectoryUnusable`] when the names cannot be read.
pub(crate) fn list_trash_in_home(home: &Path, uid: u32) -> Result<Vec<TrashedEntry>, FilesOpError> {
    let home_directory = open_home(home, uid)?;
    let Some(entries) = open_trash_subdirectory(
        &home_directory,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        uid,
        MissingParents::Require,
    )?
    else {
        return Ok(Vec::new());
    };

    let read = list_directory_names(&entries, MAXIMUM_TRASH_NAMES + 1)
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
    if read.exceeded || read.names.len() > MAXIMUM_TRASH_NAMES {
        return Err(FilesOpError::TooLarge);
    }

    let mut listed: Vec<(u64, TrashedEntry)> = Vec::new();
    for raw in &read.names {
        let Some(name) = raw.to_str() else { continue };
        let Ok(id) = TrashEntryId::parse(name) else {
            continue;
        };
        let Ok(stat) = stat_in_directory(&entries, raw) else {
            continue;
        };
        if stat.kind != EntryKind::Directory || stat.uid != uid {
            continue;
        }
        let Ok(entry) = open_in_directory(&entries, raw, DIRECTORY_FLAGS) else {
            continue;
        };
        let Ok(payload) = stat_in_directory(&entry, OsStr::new(AgentPaths::TRASH_PAYLOAD_NAME))
        else {
            continue;
        };

        let original_path = read_trash_original(&entry, uid)
            .ok()
            .filter(|text| !text.bytes().any(|byte| byte.is_ascii_control()))
            .unwrap_or_default();
        let trashed_at = id.unix_seconds();
        listed.push((
            trashed_at,
            TrashedEntry {
                trash_id: id.as_str().to_owned(),
                original_path,
                trashed_at_unix: i64::try_from(trashed_at).unwrap_or(i64::MAX),
                is_directory: payload.kind == EntryKind::Directory,
                size_bytes: if payload.kind == EntryKind::File {
                    payload.size
                } else {
                    0
                },
            },
        ));
    }

    // Newest first; the identifier breaks a tie so the order is stable.
    listed.sort_by(|left, right| {
        right
            .0
            .cmp(&left.0)
            .then_with(|| right.1.trash_id.cmp(&left.1.trash_id))
    });
    listed.truncate(MAXIMUM_LISTED_TRASH_ENTRIES);

    Ok(listed.into_iter().map(|(_, entry)| entry).collect())
}

#[cfg(test)]
#[path = "../tests/files/list_trash_in_home_tests.rs"]
mod tests;
