//! Renaming one entry to another WITHIN a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;

/// Renames `from` to `to`, both inside `directory`.
///
/// The step that makes a write atomic: readers of `to` see either the whole
/// previous file or the whole new one, never a half-written one, because the
/// rename swaps a directory entry rather than editing a file.
///
/// It is also the step that makes a write to a customer-owned directory SAFE to
/// finish. Neither name is followed as a symlink: if a hosting account planted a
/// symlink at `to` — pointing anywhere they can reach, or anywhere they cannot —
/// `renameat` REPLACES that symlink instead of writing through it. That is the
/// property the alternative lacks: opening `to` by name and truncating it would
/// follow the link and write wherever it pointed.
///
/// Both descriptors are the same one on purpose. A rename across two directory
/// descriptors is a different operation with a different failure mode
/// (`EXDEV`), and nothing here needs it: the temporary file is created in the
/// directory it will be renamed within, precisely so that the rename cannot
/// cross a filesystem and cannot be redirected by anything above it.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when either name is not a single
/// entry name (see [`directory_entry_name`]), and the operating system's error
/// when the rename is refused — notably `EISDIR`/`ENOTDIR` when `to` names a
/// directory, which is a refusal rather than a replacement.
pub fn rename_in_directory(directory: &File, from: &OsStr, to: &OsStr) -> io::Result<()> {
    let from = directory_entry_name(from)?;
    let to = directory_entry_name(to)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; both names are NUL-terminated C strings that
    // outlive it; `renameat` reads through both pointers and writes through
    // neither.
    if unsafe {
        libc::renameat(
            directory.as_raw_fd(),
            from.as_ptr(),
            directory.as_raw_fd(),
            to.as_ptr(),
        )
    } < 0
    {
        return Err(io::Error::last_os_error());
    }

    Ok(())
}
