//! Taking one entry out of the trash, or out of its copy staging area, for good.

use std::ffi::OsStr;
use std::fs::File;
use std::os::unix::fs::MetadataExt;

use crate::files::FilesOpError;
use crate::files::tree::directory_access::DirectoryAccess;
use crate::files::tree::remove_tree::remove_tree;

/// Removes the entry `name` inside `entries` and everything it holds.
///
/// `entries` is the trash's entries directory, or its staging directory: both
/// hold identifier-named directories and are cleared the same way.
///
/// A tree removal rooted one level below the entries directory, so it has every
/// property `remove_tree` has: no link followed at any depth, no other
/// filesystem entered, a depth ceiling, idempotent re-runs. The device is the
/// entries directory's own, read from its descriptor.
///
/// Always [`DirectoryAccess::Reclaimed`]: the trash and its staging area are
/// panel-managed space, and the retention purge and the emptying exist to free
/// the account's quota. A directory moved into the trash with a read-only mode
/// (`0o555`), or copied into staging with one, would otherwise hold its bytes
/// for good. Each directory entered is widened by `fchmod` on a descriptor the
/// walk has proved is a non-link directory owned by the account. The customer's
/// own force delete is a different operation and stays
/// [`DirectoryAccess::AsFound`].
///
/// # Errors
///
/// Returns the errors of `remove_tree`.
pub(crate) fn remove_trash_entry(
    entries: &File,
    name: &OsStr,
    uid: u32,
) -> Result<(), FilesOpError> {
    let device = entries
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?
        .dev();
    let mut removed = 0;

    remove_tree(
        entries,
        name,
        uid,
        device,
        0,
        DirectoryAccess::Reclaimed,
        &mut removed,
    )
}

#[cfg(test)]
#[path = "../tests/files/remove_trash_entry_tests.rs"]
mod tests;
