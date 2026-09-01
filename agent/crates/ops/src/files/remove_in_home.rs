//! Taking one file out of a customer's home without ever following a name.

use std::ffi::OsStr;
use std::io;
use std::os::unix::fs::MetadataExt;
use std::path::Path;

use maran_agent_core::privs::open_in_directory::open_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::validation::relative_path::RelativePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_parent_directory::open_parent_directory;

/// Flags the entry is opened with before it is judged.
///
/// `O_NOFOLLOW` refuses a symlink at the name, `O_NONBLOCK` makes a FIFO the
/// account left there return instead of blocking the process in the kernel
/// forever, and `O_CLOEXEC` keeps the descriptor out of anything spawned later.
/// `O_NONBLOCK` matters even though this process is a short-lived forked child:
/// a child blocked in the kernel is not killable by anything but a signal, and
/// what the parent does then is wait out its full patience before killing it —
/// two minutes of a blocking-pool thread per removal, which a customer can ask
/// for as often as they like.
const ENTRY_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;

/// Removes `relative` inside `home`, as the account owning `uid`.
///
/// Runs inside `fork_as_account`, like its writing counterpart, and reaches the
/// file the same way: [`open_parent_directory`] descends by descriptor with
/// `O_NOFOLLOW` at every level, so the directory the unlink happens in is an
/// inode and not a name a customer can redirect. The levels are REQUIRED, never
/// created — a removal that built directories on its way would be absurd, and
/// [`MissingParents`] exists so that this cannot be a mistyped flag.
///
/// The entry is then opened and judged before it is unlinked, and each of the
/// three conditions refuses a different thing a hosting customer can leave at
/// that name:
///
/// - **a regular file** — not a FIFO, a device, a socket or a directory. The
///   removal is meant to take away a challenge token, and something else at
///   that name means the panel's picture of the account's home is wrong.
/// - **owned by the account** — the claim that survives an account handing
///   write access around inside its own tree.
/// - **exactly one link** — the hardlink check. `ln` a file the account can
///   read to the challenge name and the path is genuinely inside the home, so
///   every check made against the path passes it; only the inode's link count
///   gives it away.
///
/// None of the three is a privilege boundary on its own here — the process is
/// already the account, and an account may unlink whatever it may unlink — and
/// that is exactly why they are stated as what they are: they keep the AGENT
/// from being made an instrument of a removal the panel did not ask for, and
/// they keep "the challenge was cleaned up" from being a claim about a file
/// that was never a challenge.
///
/// The unlink itself is by name inside the pinned directory, so a name swapped
/// after the checks can at worst cost the account a file it owned and could
/// have removed itself.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and
/// [`FilesOpError::DirectoryUnusable`] as [`open_parent_directory`] does;
/// [`FilesOpError::NotFound`] when the entry itself is not there — a directory
/// on the way to it being absent is [`FilesOpError::DirectoryUnusable`], not
/// this, because a challenge whose whole directory has gone is a different
/// event from one whose file has; [`FilesOpError::NotARegularFile`] when it is
/// not a plain file the account owns with a single link; and
/// [`FilesOpError::RemoveFailed`] when the unlink itself is refused.
pub(crate) fn remove_in_home(
    home: &Path,
    relative: &RelativePath,
    uid: u32,
) -> Result<(), FilesOpError> {
    let directory = open_parent_directory(home, relative, uid, MissingParents::Require)?;
    let name = OsStr::new(relative.file_name());

    let file = match open_in_directory(&directory, name, ENTRY_FLAGS) {
        Ok(file) => file,
        // Already gone is the idempotent answer `files.proto` specifies, and it
        // is told apart from every other refusal: a symlink stopped by
        // `O_NOFOLLOW` is somebody trying something, and reporting that as
        // "nothing here" would erase it.
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(FilesOpError::NotFound);
        }
        Err(_) => return Err(FilesOpError::NotARegularFile),
    };

    let metadata = file.metadata().map_err(|_| FilesOpError::NotARegularFile)?;
    if !metadata.is_file() || metadata.uid() != uid || metadata.nlink() != 1 {
        return Err(FilesOpError::NotARegularFile);
    }

    match remove_file_in_directory(&directory, name) {
        Ok(()) => Ok(()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Err(FilesOpError::NotFound),
        Err(_) => Err(FilesOpError::RemoveFailed),
    }
}

#[cfg(test)]
#[path = "../tests/files/remove_in_home_tests.rs"]
mod tests;
