//! Creating a subdirectory BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;

/// Creates a subdirectory `name` inside `directory`.
///
/// `mkdirat` never follows a symlink at the name it creates — a name that
/// already exists, symlink or not, is [`io::ErrorKind::AlreadyExists`] and
/// nothing is created — so a caller building a chain of directories inside a
/// customer's home can create each level here and then OPEN it with
/// `O_DIRECTORY | O_NOFOLLOW`, and be certain that the thing it opened is the
/// thing it made or an ordinary directory that was already there. Neither step
/// alone is enough: creating without opening leaves the next level to be
/// reached by a name, and opening without creating cannot make the level exist.
///
/// `mode` is the permission bits before the process umask is applied.
///
/// The error is the operating system's own, unflattened, because
/// [`io::ErrorKind::AlreadyExists`] is the ordinary case — the directory chain
/// this creates is created again on every renewal — and must not be confused
/// with a refusal.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error
/// otherwise, including [`io::ErrorKind::AlreadyExists`] for a name that is
/// taken.
pub fn make_directory_in_directory(
    directory: &File,
    name: &OsStr,
    mode: libc::mode_t,
) -> io::Result<()> {
    let name = directory_entry_name(name)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; `mkdirat` reads through the pointer and writes through nothing.
    if unsafe { libc::mkdirat(directory.as_raw_fd(), name.as_ptr(), mode) } < 0 {
        return Err(io::Error::last_os_error());
    }

    Ok(())
}
