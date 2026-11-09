//! Walking into the account's home one descriptor at a time, for the swap of a
//! single restored directory.

use std::ffi::OsStr;
use std::fs::{File, OpenOptions};
use std::os::unix::fs::{MetadataExt as _, OpenOptionsExt as _};
use std::path::Path;

use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::fs::relative_path::RelativePath;

use crate::backup::backup_error::BackupError;

/// Flags every directory in the walk is opened with.
///
/// `O_NOFOLLOW` refuses a symlink at that component, `O_DIRECTORY` refuses
/// anything that is not a directory, and `O_CLOEXEC` keeps the descriptor out of
/// anything spawned later. On every component and not merely the last, which is
/// what makes containment a property of the walk: no level can be a link, so no
/// level can lead out of the home.
const DIRECTORY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC;

/// Opens `path` as a directory the account `uid` owns, following no link at its
/// last component.
///
/// The one place the walk names a path. Both callers hand it a path the account
/// cannot rewrite on the way to the last component: `/home/<account>` sits under
/// root's `/home`, and the restore's working directories sit directly under the
/// root-owned restore staging root. Everything below the returned descriptor is
/// reached by `openat` instead, because everything below it is the account's to
/// rewrite.
///
/// # Errors
///
/// [`BackupError::DirectoryUnusable`] when the path is absent, is a link, is not
/// a directory, or is owned by anybody but `uid`.
pub(crate) fn open_owned_directory(path: &Path, uid: u32) -> Result<File, BackupError> {
    let opened = OpenOptions::new()
        .read(true)
        .custom_flags(DIRECTORY_FLAGS)
        .open(path)
        .map_err(|_| BackupError::DirectoryUnusable)?;

    require_owned(&opened, uid)?;

    Ok(opened)
}

/// Opens the directory that holds `relative`'s last component, descending from
/// `home` through its parent components.
///
/// Each level is reached from the descriptor above it with `openat` and
/// `O_NOFOLLOW`, and must be a directory the account owns. Ownership and not
/// merely permission, because an account can hand write access around inside its
/// own home; owning the level is the claim that survives. The walk never creates
/// a level: a single-directory restore replaces a directory that is meant to be
/// there, and a missing parent is a finding, not something to build.
///
/// This is the second descent of its kind in the agent, and the first one is
/// private to the file manager on purpose (nothing outside that area may start a
/// customer-file write by another route). This one only ever renames entries
/// between directories it holds open, as the account, and creates and deletes
/// nothing; it is kept in this area for that reason and named in the restore's
/// threat note so a reviewer weighs the two walks together.
///
/// # Errors
///
/// [`BackupError::DirectoryUnusable`] when the home or any level below it is
/// absent, is a link, is not a directory, or is not the account's.
pub(crate) fn open_parent_of(
    home: &Path,
    relative: &RelativePath,
    uid: u32,
) -> Result<File, BackupError> {
    let mut directory = open_owned_directory(home, uid)?;

    for component in relative.parent_components() {
        let next = open_in_directory(&directory, OsStr::new(component.as_str()), DIRECTORY_FLAGS)
            .map_err(|_| BackupError::DirectoryUnusable)?;
        require_owned(&next, uid)?;
        directory = next;
    }

    Ok(directory)
}

/// Refuses a descriptor that is not a directory owned by `uid`.
///
/// # Errors
///
/// [`BackupError::DirectoryUnusable`].
fn require_owned(directory: &File, uid: u32) -> Result<(), BackupError> {
    let metadata = directory
        .metadata()
        .map_err(|_| BackupError::DirectoryUnusable)?;

    if metadata.is_dir() && metadata.uid() == uid {
        Ok(())
    } else {
        Err(BackupError::DirectoryUnusable)
    }
}

#[cfg(test)]
#[path = "../tests/backup/open_restore_directory_tests.rs"]
mod tests;
