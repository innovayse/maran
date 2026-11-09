//! Putting one file into a customer's home without ever following a name.

use std::ffi::OsStr;
use std::fs::{File, Permissions};
use std::io::Write;
use std::os::unix::fs::PermissionsExt;
use std::path::Path;

use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::rename_in_directory::rename_in_directory;
use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;

use crate::files::FilesOpError;
use crate::files::io_failure::io_failure;
use crate::files::model::limit_kind::LimitKind;
use crate::files::model::missing_parents::MissingParents;
use crate::files::model::upload_source::UploadSource;
use crate::files::model::write_file_input::WriteFileInput;
use crate::files::open_mutable_parent::open_mutable_parent;
use crate::files::rename_failure::rename_failure;

/// Permission bits the temporary file is CREATED with, before it is published.
///
/// Not the caller's mode, and not a value the caller can influence at all. The
/// file is widened to the requested [`FileMode`] on its own descriptor before the
/// rename, so creating it at `0o600` means the content is never, at any instant,
/// readable by anybody but the account.
///
/// **And that is still true of this constant, so it is written down rather than
/// left for a mutation table to imply otherwise.** Changing `0o600` to `0o644`
/// here is invisible to every test, and always will be: the only difference is
/// the permissions the file carries during the microseconds between `openat` and
/// `fchmod`, which no test in this process can observe. The narrow value is
/// chosen because a file that is still being uploaded should not be world
/// readable even for an instant, and a crash in that window should leave a
/// private file rather than a public one.
const CREATE_MODE: libc::mode_t = 0o600;

/// Streams an upload into a new file at `path` inside `home`, as the account owning `uid`.
///
/// Runs inside the forked child, so the process performing every step below is the
/// customer and not root. The steps, and what each one refuses:
///
/// 1. **Descend by descriptor, creating what is missing.** [`open_mutable_parent`]
///    walks from the home with `O_NOFOLLOW` at every level, each required to be a
///    directory the account owns, so no component can be a symlink and none can be
///    swapped between the check and the use.
/// 2. **Create a new temporary file, exclusively.** `O_CREAT | O_EXCL` refuses a
///    name that is already taken, by a file or by a symlink whether or not it
///    resolves, so the bytes cannot go through a descriptor a customer opened
///    first and cannot land wherever a link they planted points.
/// 3. **Copy the upload in, counting.** Every chunk is added to a running total
///    BEFORE it is written, and the chunk that would cross `input.limit` is refused
///    with [`FilesOpError::LimitReached`]: the cap is a bound on what lands on
///    disk and not a report on what already did. The source ending without its
///    terminator ([`UploadAborted`](crate::files::model::upload_aborted::UploadAborted))
///    is a failed write, never a short file.
/// 4. **Set the mode on the descriptor, `fsync`, then publish by rename.** The
///    rename is atomic, so a reader sees the whole file or the previous one.
///    With `fail_if_exists` the rename cannot replace anything (the check and the
///    publish are one syscall, with no window between them); without it, the
///    rename REPLACES whatever name is at the destination instead of writing
///    through it, and a destination that is a directory is refused by the kernel
///    rather than emptied.
///
/// Every failure after the temporary file exists takes it away again: a
/// half-written `.maran-write-*` left in a customer's directory is litter they
/// cannot explain.
///
/// `temporary_name` is supplied by the caller because this function runs in a
/// forked child of a multi-threaded daemon, where the less it does the better.
///
/// Returns the number of bytes written.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and [`FilesOpError::DirectoryUnusable`]
/// as [`open_mutable_parent`] does; [`FilesOpError::LimitReached`] when the upload
/// is longer than `input.limit`; [`FilesOpError::QuotaExceeded`] when the account's quota
/// ends the write; [`FilesOpError::AlreadyExists`] when `fail_if_exists` is set and
/// the name is taken; and [`FilesOpError::WriteFailed`] when the file cannot be
/// created, written (including an upload that did not arrive whole), synced or
/// renamed into place.
pub(crate) fn write_in_home(
    home: &Path,
    input: &WriteFileInput,
    temporary_name: &str,
    uid: u32,
    source: &mut dyn UploadSource,
) -> Result<u64, FilesOpError> {
    let directory = open_mutable_parent(home, &input.path, uid, MissingParents::Create)?;

    let temporary = OsStr::new(temporary_name);
    let mut file = create_file_in_directory(&directory, temporary, CREATE_MODE)
        .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;

    // From here on the temporary file exists, so every failure has to take it
    // away again. Best effort, and its failure is deliberately not reported: the
    // operation already failed for a reason worth reporting, and replacing that
    // reason with "cleanup failed" would hide it.
    let finished = fill_and_place(&mut file, source, input, &directory, temporary);
    if finished.is_err() {
        let _ = remove_file_in_directory(&directory, temporary);
    }

    finished
}

/// Fills the temporary file from the upload, gives it its mode, and renames it into place.
///
/// Split out so that the caller above has exactly one place to hang the cleanup
/// of the temporary file on.
///
/// # Errors
///
/// As documented on [`write_in_home`], from the write onward.
fn fill_and_place(
    file: &mut File,
    source: &mut dyn UploadSource,
    input: &WriteFileInput,
    directory: &File,
    temporary: &OsStr,
) -> Result<u64, FilesOpError> {
    let mut written: u64 = 0;
    loop {
        let chunk = match source.next_chunk() {
            Ok(Some(chunk)) => chunk,
            Ok(None) => break,
            Err(_aborted) => return Err(FilesOpError::WriteFailed),
        };
        // Counted before it is written; saturating, so no sum can wrap into "under the cap".
        let length = u64::try_from(chunk.len()).unwrap_or(u64::MAX);
        if written.saturating_add(length) > input.limit.bytes() {
            return Err(FilesOpError::LimitReached(LimitKind::Bytes));
        }
        file.write_all(chunk)
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;
        written += length;
    }

    file.set_permissions(Permissions::from_mode(input.mode.bits()))
        .map_err(|_| FilesOpError::WriteFailed)?;

    // Before the rename, not after: publishing a name whose content is still only
    // in the page cache is how a reboot leaves an empty file at a path a reader is
    // already fetching. A quota that bites at write-back surfaces here.
    file.sync_all()
        .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;

    let name = OsStr::new(input.path.file_name());
    if input.fail_if_exists {
        rename_no_replace_between_directories(directory, temporary, directory, name)
            .map_err(|error| rename_failure(&error))?;
    } else {
        rename_in_directory(directory, temporary, name)
            .map_err(|error| io_failure(&error, FilesOpError::WriteFailed))?;
    }

    Ok(written)
}

#[cfg(test)]
#[path = "../tests/files/write_in_home_tests.rs"]
mod tests;
