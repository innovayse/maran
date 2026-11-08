//! Opening the account's home, the one name in a walk.

use std::fs::{File, OpenOptions};
use std::os::unix::fs::{MetadataExt, OpenOptionsExt};
use std::path::Path;

use crate::files::FilesOpError;

/// Flags every directory in the walk is opened with.
///
/// `O_NOFOLLOW` refuses a symlink AT that component, and `O_CLOEXEC` keeps the
/// descriptor out of anything the agent spawns. `O_NOFOLLOW` on every component,
/// and not merely on the last, is what makes the containment a property of the
/// walk: no level of the descent can be a link, so no level can point outside
/// the home.
///
/// **`O_DIRECTORY` and the `is_dir()` check below are jointly, not
/// individually, observable, and this is where that is written down rather than
/// left for a reviewer to discover.** Either one alone can be deleted with no
/// test going red, because the other still refuses. Deleting BOTH is caught, but
/// only by `open_parent_directory`'s answer for a plain file at the LAST
/// level — anywhere higher, the next syscall on the opened descriptor returns
/// `ENOTDIR` and produces the same error, so the kernel, not either check, is
/// what makes the walk safe in the middle. The pair is kept because a refusal
/// that arrives at the level it belongs to is worth more than one that arrives
/// two syscalls later, and because `is_dir()` sits beside the ownership check
/// that IS individually observable; it is not kept because either half is load
/// bearing on its own. The test that catches the pair is
/// `a_plain_file_at_the_last_level_is_refused_by_the_walk_and_not_by_a_later_syscall`.
pub(crate) const DIRECTORY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_DIRECTORY | libc::O_NOFOLLOW | libc::O_CLOEXEC;

/// Opens the account's home and proves it is one.
///
/// The single point in the walk where a path is named, and it is the one path a
/// hosting account cannot rewrite: `/home` is root-owned and not writable by
/// them, so they cannot replace `/home/<account>` with a link to somewhere
/// else. Everything below this descriptor is reached by `openat` precisely
/// because everything below it IS theirs to rewrite.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] for every refusal: absent, not a
/// directory, a symlink, or owned by somebody other than the account.
pub(crate) fn open_home(home: &Path, uid: u32) -> Result<File, FilesOpError> {
    let opened = OpenOptions::new()
        .read(true)
        .custom_flags(DIRECTORY_FLAGS)
        .open(home)
        .map_err(|_| FilesOpError::HomeUnusable)?;

    let metadata = opened.metadata().map_err(|_| FilesOpError::HomeUnusable)?;
    if !metadata.is_dir() || metadata.uid() != uid {
        return Err(FilesOpError::HomeUnusable);
    }

    Ok(opened)
}
