//! Opening one file in a customer's home for reading, as the account.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::fs::MetadataExt;
use std::path::Path;

use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use crate::files::FilesOpError;
use crate::files::open_directory_in_home::open_directory_in_home;

/// Flags the file is opened with before it is judged.
///
/// `O_NOFOLLOW` refuses a symlink at the name. `O_NONBLOCK` makes a FIFO the
/// account left there return instead of blocking the child in the kernel until
/// the parent's patience ends: two minutes of a blocking-pool thread per request
/// the customer may repeat, so the flag is a denial-of-service control. It is
/// harmless on a regular file, which is the only thing kept. `O_CLOEXEC` keeps
/// the descriptor out of anything spawned later.
const FILE_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// Opens `path` inside `home` for reading and returns the open descriptor.
///
/// Runs inside `fork_as_account_for_descriptor`, as the account. The directory
/// holding the file is reached by [`open_directory_in_home`] (a descent by
/// descriptor with `O_NOFOLLOW` and an ownership check at every level), then
/// the file is opened by name inside that directory and judged on its own
/// descriptor — never by a path:
///
/// - **a regular file** — a FIFO, device, socket or directory is refused;
/// - **owned by the account** — the claim that survives an account handing
///   access around inside its own tree;
/// - **exactly one link** — the hardlink check. `ln /etc/passwd ~/x` puts a
///   genuinely in-home name on a file elsewhere, so every check on the path
///   passes it and only the inode's link count gives it away. A customer's own
///   two-name file is refused with it, as `DeleteEntry` does.
///
/// The parent checks the received descriptor again; neither check relies on the
/// other.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as the walk does; [`FilesOpError::NotFound`] when the file is not there;
/// [`FilesOpError::NotARegularFile`] for the home itself, a symlink, or
/// anything that fails the three conditions above.
pub(crate) fn read_in_home(home: &Path, path: &BrowsePath, uid: u32) -> Result<File, FilesOpError> {
    // The home has no parent, and it is a directory: nothing to read.
    let Some(parent) = path.parent() else {
        return Err(FilesOpError::NotARegularFile);
    };
    let directory = open_directory_in_home(home, &parent, uid)?;

    let file = match open_in_directory(&directory, OsStr::new(path.file_name()), FILE_FLAGS) {
        Ok(file) => file,
        // Told apart from every other refusal: a symlink stopped by
        // `O_NOFOLLOW` is somebody trying something, and "nothing here" would
        // erase it.
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::NotARegularFile),
    };

    let metadata = file.metadata().map_err(|_| FilesOpError::NotARegularFile)?;
    if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
        return Err(FilesOpError::NotARegularFile);
    }

    Ok(file)
}

#[cfg(test)]
#[path = "../tests/files/read_in_home_tests.rs"]
mod tests;
