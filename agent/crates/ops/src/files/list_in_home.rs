//! Reading one directory of a customer's home, as the account.

use std::ffi::OsStr;
use std::path::Path;

use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use crate::files::FilesOpError;
use crate::files::model::listed_entries::{ListedEntries, MAXIMUM_LISTED_ENTRIES};
use crate::files::model::listed_entry::ListedEntry;
use crate::files::model::listing_answer::ListingAnswer;
use crate::files::open_directory_in_home::open_directory_in_home;
use crate::files::representable_name::representable_name;

/// Reads the directory `path` names inside `home`, as the account owning `uid`.
///
/// Runs inside the forked child. The steps, and what each refuses:
///
/// 1. **Descend by descriptor** ([`open_directory_in_home`]): no component is a
///    symlink and none can be swapped between the check and the use.
/// 2. **Read the names** with `fdopendir` on that descriptor, at most
///    [`MAXIMUM_LISTED_ENTRIES`] of them. One more is [`ListingAnswer::TooLarge`]
///    and nothing is listed.
/// 3. **`lstat` every name** (`fstatat` with `AT_SYMLINK_NOFOLLOW`, forced by
///    `stat_in_directory`). **No entry is ever opened**, so a FIFO with no
///    writer or a device node costs nothing, and a symlink to `/etc/shadow`
///    lists as a link with its own size, never with its target's. The target
///    text is not read at all.
/// 4. **Filter.** Hidden entries are dropped unless asked for. A name that is
///    not UTF-8 or holds a control character is skipped and counted: it could
///    not be named by any later request, and must not be silently invisible.
///
/// An entry that vanishes between the read and its `lstat` is skipped without
/// being counted: it is a race the customer won, not a name the panel cannot
/// address.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as [`open_directory_in_home`] does, and [`FilesOpError::DirectoryUnusable`]
/// when the names cannot be read or an entry cannot be `lstat`ed for a reason
/// other than being gone.
pub(crate) fn list_in_home(
    home: &Path,
    path: &BrowsePath,
    include_hidden: bool,
    uid: u32,
) -> Result<ListingAnswer, FilesOpError> {
    let directory = open_directory_in_home(home, path, uid)?;

    let read = list_directory_names(&directory, MAXIMUM_LISTED_ENTRIES + 1)
        .map_err(|_| FilesOpError::DirectoryUnusable)?;
    if read.exceeded || read.names.len() > MAXIMUM_LISTED_ENTRIES {
        return Ok(ListingAnswer::TooLarge);
    }

    let mut entries = Vec::with_capacity(read.names.len());
    let mut skipped: u32 = 0;
    for raw in &read.names {
        // The directory's own links are not entries; a reader that reports them
        // would make `lstat` refuse the whole listing on `..`.
        if raw == OsStr::new(".") || raw == OsStr::new("..") {
            continue;
        }
        let Some(name) = representable_name(raw) else {
            skipped = skipped.saturating_add(1);
            continue;
        };
        if !include_hidden && name.starts_with('.') {
            continue;
        }
        let stat = match stat_in_directory(&directory, raw) {
            Ok(stat) => stat,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(_) => return Err(FilesOpError::DirectoryUnusable),
        };
        entries.push(ListedEntry {
            name: name.to_owned(),
            kind: stat.kind,
            size: stat.size,
            mode: stat.mode & 0o7777,
            modified_unix: stat.modified_unix,
            uid: stat.uid,
            gid: stat.gid,
            link_count: stat.links,
        });
    }

    Ok(ListingAnswer::Entries(ListedEntries { entries, skipped }))
}

#[cfg(test)]
#[path = "../tests/files/list_in_home_tests.rs"]
mod tests;
