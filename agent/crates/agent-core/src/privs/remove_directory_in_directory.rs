//! Removing an EMPTY directory BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;

/// Removes the empty directory `name` from `directory`.
///
/// `unlinkat` with `AT_REMOVEDIR`, forced here and not a parameter. The kernel
/// refuses a non-empty directory (`ENOTEMPTY`), so this cannot recurse, and it
/// refuses anything that is not a directory (`ENOTDIR`) — including a symlink to
/// one, which is not followed. Emptying a tree is the caller's job, entry by
/// entry, through descriptors; taking the whole thing away in one call is a
/// decision this function cannot be made to take by accident.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error
/// otherwise — `ENOTEMPTY` for a directory that still holds entries, `ENOTDIR`
/// for a non-directory, [`io::ErrorKind::NotFound`] for a name already gone.
pub fn remove_directory_in_directory(directory: &File, name: &OsStr) -> io::Result<()> {
    let name = directory_entry_name(name)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; `unlinkat` reads through the pointer and writes through nothing.
    // `AT_REMOVEDIR` is a constant, not caller input.
    if unsafe { libc::unlinkat(directory.as_raw_fd(), name.as_ptr(), libc::AT_REMOVEDIR) } < 0 {
        return Err(io::Error::last_os_error());
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/privs/remove_directory_in_directory_tests.rs"]
mod tests;
