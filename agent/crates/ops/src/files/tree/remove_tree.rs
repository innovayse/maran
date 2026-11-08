//! Taking an entry, and everything below it, out of a tree.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io;
use std::os::unix::fs::PermissionsExt;

use maran_agent_core::privs::entry_kind::EntryKind;
use maran_agent_core::privs::list_directory_names::list_directory_names;
use maran_agent_core::privs::remove_directory_in_directory::remove_directory_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::files::FilesOpError;
use crate::files::model::limit_kind::LimitKind;
use crate::files::tree::directory_access::DirectoryAccess;
use crate::files::tree::open_tree_directory::open_tree_directory;
use crate::files::tree::tree_limits::TreeLimits;

/// Names read from one directory per pass.
///
/// A directory with more entries than this is emptied in several passes, each
/// of which opens the directory afresh, so the memory spent on names is bounded
/// by this and not by what a customer put in the directory.
const NAMES_PER_PASS: usize = 4096;

/// Permission bits a [`DirectoryAccess::Reclaimed`] directory is given before it is emptied.
const RECLAIMED_MODE: u32 = 0o700;

/// Removes `name` inside `parent` and, for a directory, everything below it.
///
/// `unlinkat` by name inside a pinned directory descriptor, directories emptied
/// depth first through descriptors, and `AT_REMOVEDIR` only on a directory the
/// walk has just emptied (`remove_directory_in_directory` refuses a non-empty
/// one). Nothing is followed: a symlink is unlinked and its target survives,
/// a hardlinked file is unlinked by name and the other names keep the data, and
/// a FIFO or device is unlinked and never opened. A directory is entered only
/// through `open_tree_directory`, so one on another filesystem than `device`
/// (a mount inside the tree) or owned by somebody else is refused instead of
/// widening the removal.
///
/// A tree deeper than [`TreeLimits::MAXIMUM_DEPTH`] is refused with
/// [`FilesOpError::TooDeep`] at the level where the ceiling is crossed, and
/// more than [`TreeLimits::CEILING_ENTRIES`] removals in one call stop with
/// [`FilesOpError::LimitReached`]. Neither is a rollback: what was removed stays
/// removed, and because the operation is idempotent a second run finishes what
/// the first left, which is also what makes a killed run safe.
///
/// `access` says what to do with a directory whose mode forbids emptying it:
/// [`DirectoryAccess::AsFound`] leaves it and the unlink is refused,
/// [`DirectoryAccess::Reclaimed`] sets it to `0o700` first (by `fchmod` on the
/// descriptor just opened and checked, never by name).
///
/// `removed` is a running count of entries taken away, the root included, kept
/// by the caller so it survives an error.
///
/// # Errors
///
/// Returns [`FilesOpError::NotFound`] when the root itself is not there;
/// [`FilesOpError::TooDeep`] and [`FilesOpError::LimitReached`] as above;
/// [`FilesOpError::NotOwned`], [`FilesOpError::CrossesDevice`] and
/// [`FilesOpError::DirectoryUnusable`] as `open_tree_directory` does; and
/// [`FilesOpError::RemoveFailed`] when an unlink, an `rmdir` or the widening of
/// a reclaimed directory is refused.
pub(crate) fn remove_tree(
    parent: &File,
    name: &OsStr,
    uid: u32,
    device: u64,
    depth: usize,
    access: DirectoryAccess,
    removed: &mut u64,
) -> Result<(), FilesOpError> {
    let stat = match stat_in_directory(parent, name) {
        Ok(stat) => stat,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return if depth == 0 {
                Err(FilesOpError::NotFound)
            } else {
                // Taken away by somebody else after the directory was read:
                // exactly what this was about to do.
                Ok(())
            };
        }
        Err(_) => return Err(FilesOpError::RemoveFailed),
    };

    if *removed >= TreeLimits::CEILING_ENTRIES {
        return Err(FilesOpError::LimitReached(LimitKind::Entries));
    }

    match stat.kind {
        EntryKind::File | EntryKind::Symlink | EntryKind::Other => {
            unlink(parent, name)?;
            *removed = removed.saturating_add(1);
        }
        EntryKind::Directory => {
            if depth >= TreeLimits::MAXIMUM_DEPTH {
                return Err(FilesOpError::TooDeep);
            }
            empty_directory(parent, name, uid, device, depth, access, removed)?;
            match remove_directory_in_directory(parent, name) {
                Ok(()) => *removed = removed.saturating_add(1),
                Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                Err(_) => return Err(FilesOpError::RemoveFailed),
            }
        }
    }

    Ok(())
}

/// Unlinks one non-directory entry; one already gone is not a failure.
fn unlink(parent: &File, name: &OsStr) -> Result<(), FilesOpError> {
    match remove_file_in_directory(parent, name) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(_) => Err(FilesOpError::RemoveFailed),
    }
}

/// Removes every entry of the directory `name`, in passes, until it is empty.
///
/// The directory is opened afresh for every pass so no pass depends on where a
/// previous one left a read position, and a pass that finds nothing is the end.
/// Every pass removes at least the names it read or fails, so the loop ends; a
/// customer adding files as fast as they are removed is bounded by the child's
/// patience, which kills it, and by the entry ceiling.
fn empty_directory(
    parent: &File,
    name: &OsStr,
    uid: u32,
    device: u64,
    depth: usize,
    access: DirectoryAccess,
    removed: &mut u64,
) -> Result<(), FilesOpError> {
    loop {
        let directory = open_tree_directory(parent, name, uid, device)?;
        if access == DirectoryAccess::Reclaimed {
            directory
                .set_permissions(Permissions::from_mode(RECLAIMED_MODE))
                .map_err(|_| FilesOpError::RemoveFailed)?;
        }
        let read = list_directory_names(&directory, NAMES_PER_PASS)
            .map_err(|_| FilesOpError::DirectoryUnusable)?;

        let mut any = false;
        for child in &read.names {
            if child == OsStr::new(".") || child == OsStr::new("..") {
                continue;
            }
            any = true;
            remove_tree(&directory, child, uid, device, depth + 1, access, removed)?;
        }
        if !any {
            return Ok(());
        }
    }
}

#[cfg(test)]
#[path = "../../tests/files/tree/remove_tree_tests.rs"]
mod tests;
