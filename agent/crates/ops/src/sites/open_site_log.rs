//! Opening one log file through a pinned directory and proving it is worth reading.

use std::ffi::OsStr;
use std::fs::{File, Metadata};
use std::io;
use std::os::unix::fs::MetadataExt;

use maran_agent_core::privs::open_in_directory::open_in_directory;

use crate::sites::SitesOpError;

/// Flags every open of a log itself carries.
///
/// `O_NOFOLLOW` refuses a symlink in the final component, `O_NONBLOCK` makes a
/// FIFO return instead of blocking a root thread in the kernel, and
/// `O_CLOEXEC` keeps the descriptor out of anything the agent spawns later.
const LOG_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// Opens the log through `directory` and proves it is a file worth reading.
///
/// Returns `None` when there is no log yet, which is the ordinary state of a
/// site that has served no request. Every other refusal is an error: a symlink
/// stopped by `O_NOFOLLOW`, a FIFO, a device, a directory, a file `uid` does not
/// own, or one with a second link — reported rather than quietly treated as
/// "nothing here", because each of those is somebody trying something, and at
/// this location somebody with root already.
///
/// Shared by the log tail and the traffic summary, so the discipline below is
/// stated once and a refusal added to it is a refusal of both.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] for anything other than a regular
/// file owned by `uid` with exactly one link, or a file that does not exist.
pub(crate) fn open_site_log(
    directory: &File,
    file_name: &OsStr,
    uid: u32,
) -> Result<Option<(File, Metadata)>, SitesOpError> {
    let file = match open_in_directory(directory, file_name, LOG_FLAGS) {
        Ok(file) => file,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(None),
        Err(_) => return Err(SitesOpError::unreadable_log(file_name)),
    };

    let metadata = file
        .metadata()
        .map_err(|_| SitesOpError::unreadable_log(file_name))?;

    // `is_file` is the FIFO check and the device check and the directory check,
    // all three: `O_NONBLOCK` is what stopped the open of a FIFO from hanging,
    // and this is what stops us reading one. `nlink == 1` is the hardlink
    // check — `ln /etc/shadow access.log` is not a symlink and lives at exactly
    // the path this module names, so only the inode gives it away. It stays
    // true under the shipped rotation, which signals the nginx master to reopen
    // rather than copying the live file aside.
    if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
        return Err(SitesOpError::unreadable_log(file_name));
    }

    Ok(Some((file, metadata)))
}

#[cfg(test)]
#[path = "../tests/sites/open_site_log_tests.rs"]
mod tests;
