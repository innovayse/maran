//! Opening a file BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::{AsRawFd, FromRawFd};

use super::directory_entry_name::directory_entry_name;

/// Opens `name` inside `directory` and nowhere else.
///
/// The counterpart of [`crate::validation::path::resolve_in_home`], and the
/// reason that function's warning can be obeyed at all. `resolve_in_home`'s own
/// doc says it: *"resolving and then reopening by the original path would
/// reintroduce the race this function exists to close"*. A long-lived reader —
/// a log tail is the only one today — must therefore resolve ONCE, keep the
/// directory open, and reach the file through this: a file descriptor names an
/// inode, so no rename, no `rmdir` and no symlink planted where the directory
/// used to be can redirect a later open. `O_NOFOLLOW` on the final component
/// then covers the only part of the path that is still a name.
///
/// `flags` is the caller's, and every caller of this passes `O_NOFOLLOW`. It is
/// not forced here because the flag set a safe open needs differs by what is
/// being opened — a tail also needs `O_NONBLOCK`, so that a FIFO the account
/// left in place of its log returns instead of blocking a root thread in the
/// kernel forever — and a function that silently added flags would hide which
/// protections a given call site actually asked for.
///
/// **That freedom carries an obligation, and it is stated here rather than
/// disclaimed.** `openat` cannot return `EINTR` for the flags every current
/// caller passes, because the only `open` that blocks on Linux is a FIFO
/// without `O_NONBLOCK` — so this function does not retry, and it is correct
/// not to. A caller that omits `O_NONBLOCK` takes on BOTH the hang risk and an
/// unhandled `EINTR` (which would surface as a spurious refusal, not as a
/// safety hole). Pass `O_NONBLOCK` whenever the name could be anything but a
/// regular file the caller created, which inside a customer's home is always.
///
/// This does NOT make the opened file safe to read. It proves only *which*
/// inode was opened; the caller must still `fstat` the result and refuse
/// anything that is not the regular file it expected, owned by the account it
/// expected, with no second link. That check cannot live here, because this
/// function does not know what the caller came for.
///
/// The error is the operating system's own, not a flattened one, and that is
/// deliberate: the caller must be able to tell [`io::ErrorKind::NotFound`] —
/// a log for a site that has served no request — from a refusal, which is a
/// symlink caught by `O_NOFOLLOW` or a permission the agent does not have.
/// Collapsing the two would report an attack as "nothing here yet".
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single path
/// component or contains a NUL, and the operating system's error when the
/// open is refused.
pub fn open_in_directory(directory: &File, name: &OsStr, flags: libc::c_int) -> io::Result<File> {
    // A single component and nothing else: `openat` resolves a relative path,
    // so a `name` of `../../etc/shadow` would walk out of the directory the fd
    // pins. The caller derives the name from a validated `Domain` or a
    // validated `RelativePath`, and this is the second check of that, at the
    // syscall (rules/security.md, defense in depth).
    let name = directory_entry_name(name)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; and `openat` writes through neither pointer. The returned descriptor
    // is owned by nothing until `from_raw_fd` below takes it, and on failure it
    // is -1 and is never wrapped.
    let opened = unsafe { libc::openat(directory.as_raw_fd(), name.as_ptr(), flags) };
    if opened < 0 {
        return Err(io::Error::last_os_error());
    }

    // SAFETY: `opened` is a fresh descriptor this call just created, owned by
    // nobody else, so `File` may take sole ownership of closing it.
    Ok(unsafe { File::from_raw_fd(opened) })
}

#[cfg(test)]
#[path = "../tests/privs/open_in_directory_tests.rs"]
mod tests;
