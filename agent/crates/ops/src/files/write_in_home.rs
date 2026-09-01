//! Putting one file into a customer's home without ever following a name.

use std::ffi::OsStr;
use std::fs::Permissions;
use std::io::Write;
use std::os::unix::fs::PermissionsExt;
use std::path::Path;

use maran_agent_core::privs::create_file_in_directory::create_file_in_directory;
use maran_agent_core::privs::remove_file_in_directory::remove_file_in_directory;
use maran_agent_core::privs::rename_in_directory::rename_in_directory;
use maran_agent_core::validation::file_mode::FileMode;
use maran_agent_core::validation::relative_path::RelativePath;

use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_parent_directory::open_parent_directory;

/// Permission bits the temporary file is CREATED with, before it is published.
///
/// Not the caller's mode, and not a value the caller can influence at all. The
/// file is widened to [`FileMode`] on its own descriptor before the rename, so
/// creating it at `0o600` means the content is never, at any instant, readable
/// by anybody but the account — which matters for a file whose content is a
/// single-use proof. This also replaces a line that could not be wrong: the
/// caller's mode used to be converted into a `mode_t` here, and the conversion
/// was `u32 -> u32` on every supported target while the value it produced was
/// unobservable, because the `fchmod` below overwrites it either way.
///
/// **And that is still true of this constant, so it is written down rather than
/// left for a mutation table to imply otherwise.** Changing `0o600` to `0o644`
/// here is invisible to every test, and always will be: the only difference is
/// the permissions the file carries during the microseconds between `openat` and
/// `fchmod`, which no test in this process can observe. The narrow value is
/// chosen on the argument above — a single-use proof should not be world
/// readable even for an instant, and a crash in that window should leave a
/// private file rather than a public one — and not because a test demands it.
const CREATE_MODE: libc::mode_t = 0o600;

/// Writes `contents` at `relative` inside `home`, as the account owning `uid`.
///
/// Runs inside `fork_as_account`, so the process performing every step below is
/// the customer and not root. The steps, and what each one refuses:
///
/// 1. **Descend by descriptor.** [`open_parent_directory`] walks from the home
///    to the file's directory with `O_NOFOLLOW` at every level, so no component
///    can be a symlink and no component can be swapped between the check and
///    the use. It is asked to REQUIRE the levels rather than create them: the
///    operation created them in an earlier, separate drop, and a write that
///    could also build directories is a write that can be aimed at a tree
///    nobody asked for.
/// 2. **Create a new file, exclusively.** `O_CREAT | O_EXCL` refuses a name that
///    is already taken — by a regular file, and by a symlink whether or not it
///    resolves — so the bytes cannot go through a descriptor a customer opened
///    first, and cannot land wherever a link they planted points.
/// 3. **Set the mode on the descriptor.** Not on a path, which would be a second
///    lookup and therefore a second chance to be redirected, and not left to
///    `O_CREAT`'s mode argument, which the daemon's umask silently narrows. A
///    challenge the web server cannot read is an issuance that fails with
///    nothing in any log to explain it.
/// 4. **`fsync`, then rename into place.** The rename is atomic, so a reader
///    sees the whole file or no file; and it REPLACES whatever name is at the
///    destination instead of writing through it, which is what makes finishing
///    the write safe when the customer owns the directory. A destination that is
///    a directory is refused by the kernel rather than emptied.
///
/// `temporary_name` is supplied by the caller rather than built here, and that
/// is deliberate: this function runs in a forked child of a multi-threaded
/// daemon, where the less it does the better (`fork_as_account`'s own
/// contract). Building a unique name means asking the clock and formatting a
/// string, so the parent does it before the fork, when it is safe to.
///
/// `mode` is a [`FileMode`] and not a number, so there is no mode check in this
/// function and no way to reach it with one that asks for setuid, setgid or the
/// sticky bit. That validation is a property of the type now
/// (rules/rust.md "Validation first"): it used to be two hand-written `if`s in
/// two layers, which left the EDGES of it — the wire code it produces, and
/// whether a layer refuses or quietly masks — untested and separately mutable.
///
/// # Errors
///
/// Returns [`FilesOpError::HomeUnusable`] and
/// [`FilesOpError::DirectoryUnusable`] as [`open_parent_directory`] does; and
/// [`FilesOpError::WriteFailed`] when the file cannot be created, written,
/// synced or renamed into place.
pub(crate) fn write_in_home(
    home: &Path,
    relative: &RelativePath,
    temporary_name: &str,
    contents: &[u8],
    mode: FileMode,
    uid: u32,
) -> Result<(), FilesOpError> {
    let directory = open_parent_directory(home, relative, uid, MissingParents::Require)?;

    let temporary = OsStr::new(temporary_name);
    let mut file = create_file_in_directory(&directory, temporary, CREATE_MODE)
        .map_err(|_| FilesOpError::WriteFailed)?;

    // From here on the temporary file exists, so every failure has to take it
    // away again: a half-written `.maran-*` left in a customer's document root
    // is litter the customer cannot explain and the next renewal cannot reuse.
    let finished = write_and_place(&mut file, contents, mode, &directory, temporary, relative);
    if finished.is_err() {
        // Best effort, and its failure is deliberately not reported: the
        // operation already failed for a reason worth reporting, and replacing
        // that reason with "cleanup failed" would hide it.
        let _ = remove_file_in_directory(&directory, temporary);
    }

    finished
}

/// Fills the temporary file, gives it its mode, and renames it into place.
///
/// Split out so that the caller above has exactly one place to hang the cleanup
/// of the temporary file on. Inline, the cleanup would have to be repeated at
/// four `?` sites, and a fifth added later would not have it.
///
/// # Errors
///
/// Returns [`FilesOpError::WriteFailed`] when the write, the mode change, the
/// `fsync` or the rename fails.
fn write_and_place(
    file: &mut std::fs::File,
    contents: &[u8],
    mode: FileMode,
    directory: &std::fs::File,
    temporary: &OsStr,
    relative: &RelativePath,
) -> Result<(), FilesOpError> {
    file.write_all(contents)
        .map_err(|_| FilesOpError::WriteFailed)?;

    file.set_permissions(Permissions::from_mode(mode.bits()))
        .map_err(|_| FilesOpError::WriteFailed)?;

    // Before the rename, not after: the rename is what publishes the file, and
    // publishing a name whose content is still only in the page cache is how a
    // reboot leaves an empty challenge at a path the authority is already
    // fetching.
    file.sync_all().map_err(|_| FilesOpError::WriteFailed)?;

    rename_in_directory(directory, temporary, OsStr::new(relative.file_name()))
        .map_err(|_| FilesOpError::WriteFailed)
}

#[cfg(test)]
#[path = "../tests/files/write_in_home_tests.rs"]
mod tests;
