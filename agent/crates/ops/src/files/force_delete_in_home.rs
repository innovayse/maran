//! Removing a file, a link or a whole tree from a customer's home, as the customer.

use std::ffi::OsStr;
use std::os::unix::fs::MetadataExt;
use std::path::Path;

use maran_agent_core::privs::stat_in_directory::stat_in_directory;
use maran_agent_core::validation::fs::mutable_path::MutablePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::tree::directory_access::DirectoryAccess;
use crate::files::tree::remove_tree::remove_tree;

/// Removes `path` inside `home`, a whole tree if it is a directory, as the
/// account owning `uid`, and answers how many entries were taken away.
///
/// Not [`remove_in_home`](super::remove_in_home), which stays the narrow ACME
/// cleanup and refuses everything but a single-link regular file; this is the
/// file manager's delete and removes links, hardlinked files and trees. What
/// makes that safe is `remove_tree`: unlink by name inside a pinned descriptor,
/// directories emptied depth first, no link followed at any depth, no other
/// filesystem entered, a depth ceiling.
///
/// The root must be owned by the account, the claim that survives an account
/// handing write access around; a symlink root is unlinked itself (its owner is
/// whoever made it, and the target is never looked at). The device the root
/// must stay on is its parent directory's, so a mount point as the root is
/// refused rather than attempted.
///
/// Idempotent in the sense the agent can offer: a path that is gone is
/// [`FilesOpError::NotFound`], and a run that was stopped or refused part-way by
/// the entry ceiling is finished by running it again.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as `open_mutable_parent` does, [`FilesOpError::NotFound`] when the entry is
/// not there, [`FilesOpError::NotOwned`] when the account does not own it, and
/// the errors of `remove_tree`.
pub(crate) fn force_delete_in_home(
    home: &Path,
    path: &MutablePath,
    uid: u32,
) -> Result<u64, FilesOpError> {
    let parent = open_mutable_parent(home, path, uid, MissingParents::Require)?;
    let name = OsStr::new(path.file_name());

    match stat_in_directory(&parent, name) {
        Ok(stat) if stat.uid != uid => return Err(FilesOpError::NotOwned),
        Ok(_) => {}
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::RemoveFailed),
    }

    let device = parent
        .metadata()
        .map_err(|_| FilesOpError::DirectoryUnusable)?
        .dev();
    let mut removed = 0;
    remove_tree(
        &parent,
        name,
        uid,
        device,
        0,
        DirectoryAccess::AsFound,
        &mut removed,
    )?;

    Ok(removed)
}

#[cfg(test)]
#[path = "../tests/files/force_delete_in_home_tests.rs"]
mod tests;
