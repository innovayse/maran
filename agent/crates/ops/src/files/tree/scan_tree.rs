//! Measuring a tree before anything is done to it.

use std::ffi::OsStr;
use std::fs::File;
use std::io;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;
use crate::files::tree::open_tree_directory::open_tree_directory;
use crate::files::tree::tree_limits::TreeLimits;
use crate::files::tree::tree_tally::TreeTally;

/// Counts the entry `name` inside `parent` and, for a directory, everything below it.
///
/// **No entry is ever opened except a directory being entered.** A file is
/// `fstatat`ed by name with `AT_SYMLINK_NOFOLLOW` (so a FIFO with no writer
/// costs nothing and a link is a link), and a directory is entered through
/// `open_tree_directory`, which refuses a swapped name. The result is a tally and
/// not a verdict: whether a link, a mount point or a foreign directory refuses
/// the operation is the caller's decision (a copy refuses, a permission change
/// skips and reports).
///
/// `device` is the filesystem of the operation's root and `depth` the number of
/// directories above this entry within the walk (zero for the root). The bounds
/// are enforced as the walk counts, so a tree that is far over its allowance
/// costs reading at most that allowance.
///
/// An entry that vanishes between the directory read and its `fstatat` is a race
/// the customer won and is not counted.
///
/// # Errors
///
/// Returns [`FilesOpError::LimitReached`] when the entries or bytes counted pass
/// `limits`, [`FilesOpError::TooDeep`] when a directory would be entered more
/// than [`TreeLimits::MAXIMUM_DEPTH`] levels down, and
/// [`FilesOpError::DirectoryUnusable`] when a directory that should have been
/// enterable cannot be read.
pub(crate) fn scan_tree(
    parent: &File,
    name: &OsStr,
    uid: u32,
    device: u64,
    depth: usize,
    limits: &TreeLimits,
    tally: &mut TreeTally,
) -> Result<(), FilesOpError> {
    let stat = match stat_in_directory(parent, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(_) => return Err(FilesOpError::DirectoryUnusable),
    };

    tally.entries = tally.entries.saturating_add(1);
    if tally.entries > limits.max_entries {
        return Err(FilesOpError::LimitReached(LimitKind::Entries));
    }

    match stat.kind {
        EntryKind::File => {
            tally.bytes = tally.bytes.saturating_add(stat.size);
            if tally.bytes > limits.max_bytes {
                return Err(FilesOpError::LimitReached(LimitKind::Bytes));
            }
        }
        EntryKind::Symlink | EntryKind::Other => {
            tally.unsupported = tally.unsupported.saturating_add(1);
        }
        EntryKind::Directory => {
            if stat.device != device {
                tally.crossings = tally.crossings.saturating_add(1);
                return Ok(());
            }
            if stat.uid != uid {
                tally.foreign = tally.foreign.saturating_add(1);
                return Ok(());
            }
            if depth >= TreeLimits::MAXIMUM_DEPTH {
                return Err(FilesOpError::TooDeep);
            }

            let directory = open_tree_directory(parent, name, uid, device)?;
            let read = list_directory_names(
                &directory,
                usize::try_from(limits.max_entries)
                    .unwrap_or(usize::MAX)
                    .saturating_add(1),
            )
            .map_err(|_| FilesOpError::DirectoryUnusable)?;
            if read.exceeded {
                return Err(FilesOpError::LimitReached(LimitKind::Entries));
            }
            for child in &read.names {
                if child == OsStr::new(".") || child == OsStr::new("..") {
                    continue;
                }
                scan_tree(&directory, child, uid, device, depth + 1, limits, tally)?;
            }
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../../tests/files/tree/scan_tree_tests.rs"]
mod tests;
