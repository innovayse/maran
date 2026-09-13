//! Creating a NEW file BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::os::unix::io::{AsRawFd, FromRawFd};

use super::directory_entry_name::directory_entry_name;

/// Creates `name` inside `directory`, and fails if anything is already there.
///
/// The creating counterpart of
/// [`open_in_directory`](super::open_in_directory::open_in_directory), and the
/// flags are NOT the caller's here, which is the one deliberate difference
/// between the two. `open_in_directory` documents why it leaves the flag set to
/// its caller: a reader needs different flags for a log than for a config, and
/// hiding that would hide which protections a call site asked for. A *creation*
/// has no such spread. There is exactly one safe way to bring a new file into a
/// directory a hosting customer owns, so it is fixed here rather than restated
/// at every call site, where one omission is a symlink followed:
///
/// - `O_CREAT | O_EXCL` — the pair is what refuses a symlink at the final
///   component, and it does so more strongly than `O_NOFOLLOW`: with `O_EXCL`
///   the kernel fails on a symlink whether or not the link resolves, and it
///   fails on an existing regular file too, so an account cannot pre-create the
///   name and have the agent write through their own descriptor. `O_NOFOLLOW`
///   is passed as well, for a reader who checks one flag rather than the pair.
/// - `O_WRONLY` — nothing that creates a file here needs to read it back.
/// - `O_CLOEXEC` — the descriptor is never wanted by anything the agent spawns.
///
/// `mode` is the permission bits the file is created with, before the process
/// umask is applied to them. The umask is the parent daemon's and is therefore
/// not a caller's business, which is why every caller that has an exact mode to
/// land on must set it on the returned descriptor rather than trusting this
/// argument alone.
///
/// Like `open_in_directory`, this proves only WHICH inode was created — a fresh
/// one, in this directory. It cannot prove the directory is the right one; the
/// caller reached it through descriptors and is responsible for that.
///
/// The error is the operating system's own, unflattened, so that a caller can
/// tell [`io::ErrorKind::AlreadyExists`] — a name a customer occupied first —
/// from a permission failure or a read-only filesystem.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error when
/// the creation is refused.
pub fn create_file_in_directory(
    directory: &File,
    name: &OsStr,
    mode: libc::mode_t,
) -> io::Result<File> {
    let name = directory_entry_name(name)?;

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; and `openat` writes through neither pointer. The variadic `mode`
    // argument is read because `O_CREAT` is set, and `mode_t` is the type the
    // C library expects there. On failure the return is -1 and is never wrapped.
    let opened = unsafe {
        libc::openat(
            directory.as_raw_fd(),
            name.as_ptr(),
            libc::O_CREAT | libc::O_EXCL | libc::O_WRONLY | libc::O_NOFOLLOW | libc::O_CLOEXEC,
            libc::c_uint::from(mode),
        )
    };
    if opened < 0 {
        return Err(io::Error::last_os_error());
    }

    // SAFETY: `opened` is a fresh descriptor this call just created, owned by
    // nobody else, so `File` may take sole ownership of closing it.
    Ok(unsafe { File::from_raw_fd(opened) })
}
