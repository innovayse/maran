//! Unlinking an entry BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;

/// Unlinks `name` from `directory`.
///
/// `unlinkat` with no flags removes a directory ENTRY and never follows it, so a
/// symlink planted at `name` is itself removed rather than being resolved and
/// its target removed. That is the whole reason removal goes through a
/// descriptor and a name instead of `std::fs::remove_file` on a path: the path
/// version has the same non-following behaviour at the last component, and none
/// of it above.
///
/// A directory is refused by the kernel (`EISDIR` on Linux), which is what this
/// caller wants: taking a directory away is a different decision from taking a
/// file away, and it is not made here by accident.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error
/// otherwise — notably [`io::ErrorKind::NotFound`] for a name that is already
/// gone, which callers treat as the idempotent outcome rather than as a fault.
pub fn remove_file_in_directory(directory: &File, name: &OsStr) -> io::Result<()> {
    let name = directory_entry_name(name)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; `unlinkat` reads through the pointer and writes through nothing.
    if unsafe { libc::unlinkat(directory.as_raw_fd(), name.as_ptr(), 0) } < 0 {
        return Err(io::Error::last_os_error());
    }

    Ok(())
}
