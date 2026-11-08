//! Renaming an entry from one held directory to another, never replacing anything.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;

/// Moves `from` (inside `from_directory`) to `to` (inside `to_directory`) with
/// `renameat2(RENAME_NOREPLACE)`.
///
/// The flag is forced here and is not a parameter, for the reason the flags of
/// every sibling wrapper are: a caller that could drop it would turn a move into
/// an overwrite of whatever the customer has at the destination. Nothing is
/// replaced, and neither final name is followed as a symlink: a link at `to` makes
/// the call fail with `EEXIST`, and a link at `from` is moved as the link it is.
/// Both directories are descriptors the caller already holds, so no component
/// above either name can be swapped between a check and this use.
///
/// There is no copy-and-delete fallback: a destination on another filesystem is
/// `EXDEV`, and a filesystem that does not support the flag answers `EINVAL`.
/// Both are returned as the operating system's error for the caller to type, so a
/// move can never turn into a partial copy.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when either name is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error when the
/// rename is refused: `EEXIST` or `ENOTEMPTY` for a taken destination, `ENOENT`
/// for a missing source, `EXDEV` across filesystems, `EINVAL` for a directory
/// moved into its own subtree or an unsupported filesystem.
pub fn rename_no_replace_between_directories(
    from_directory: &File,
    from: &OsStr,
    to_directory: &File,
    to: &OsStr,
) -> io::Result<()> {
    let from = directory_entry_name(from)?;
    let to = directory_entry_name(to)?;

    // SAFETY: both descriptors belong to live `File`s, valid for the duration of
    // the call; both names are NUL-terminated C strings that outlive it; the
    // kernel reads through the two pointers and writes through neither. The
    // syscall number and argument order are those of `renameat2(2)`.
    let status = unsafe {
        libc::syscall(
            libc::SYS_renameat2,
            from_directory.as_raw_fd(),
            from.as_ptr(),
            to_directory.as_raw_fd(),
            to.as_ptr(),
            libc::RENAME_NOREPLACE,
        )
    };
    if status < 0 {
        return Err(io::Error::last_os_error());
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/privs/rename_no_replace_between_directories_tests.rs"]
mod tests;
