//! Reading an entry's ownership and kind BY NAME inside a directory already held open.

use std::ffi::OsStr;
use std::fs::File;
use std::io;
use std::mem::MaybeUninit;
use std::os::unix::io::AsRawFd;

use super::directory_entry_name::directory_entry_name;
use super::entry_kind::EntryKind;
use super::entry_stat::EntryStat;

/// Reports the owner, device, kind and link count of `name` inside `directory`.
///
/// `fstatat` with `AT_SYMLINK_NOFOLLOW`, forced here and not a parameter: the
/// answer describes the ENTRY, so a symlink planted at `name` is reported as a
/// symlink owned by whoever made it, never as its target. A caller deciding
/// "does this belong to the account being deleted" on `uid` is therefore not
/// fooled by a link into someone else's file. The flag is not the caller's to
/// drop because no caller of this wants the other behaviour, and a function that
/// could be asked to follow would make every call site re-argue it.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] when `name` is not a single entry
/// name (see [`directory_entry_name`]), and the operating system's error
/// otherwise — notably [`io::ErrorKind::NotFound`] for a name that is gone.
pub fn stat_in_directory(directory: &File, name: &OsStr) -> io::Result<EntryStat> {
    let name = directory_entry_name(name)?;
    let mut buffer = MaybeUninit::<libc::stat>::zeroed();

    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // duration of the call; `name` is a NUL-terminated C string that outlives
    // it; `buffer` is a writable, properly aligned `libc::stat` the kernel fills
    // only on success. `AT_SYMLINK_NOFOLLOW` is a constant, not caller input.
    let status = unsafe {
        libc::fstatat(
            directory.as_raw_fd(),
            name.as_ptr(),
            buffer.as_mut_ptr(),
            libc::AT_SYMLINK_NOFOLLOW,
        )
    };
    if status < 0 {
        return Err(io::Error::last_os_error());
    }

    // SAFETY: `fstatat` returned 0, so it fully initialised the buffer (which
    // was zeroed to begin with, a valid bit pattern for `libc::stat`).
    let stat = unsafe { buffer.assume_init() };

    // `st_nlink` is `u64` on x86_64 and `u32` elsewhere; the cast is the
    // portable spelling and clippy only sees the identity on one of them.
    #[allow(clippy::unnecessary_cast)]
    let links = stat.st_nlink as u64;

    Ok(EntryStat {
        uid: stat.st_uid,
        gid: stat.st_gid,
        device: stat.st_dev,
        kind: kind_of(stat.st_mode),
        links,
    })
}

/// Classifies the `S_IFMT` bits of an `st_mode`.
fn kind_of(mode: libc::mode_t) -> EntryKind {
    match mode & libc::S_IFMT {
        libc::S_IFREG => EntryKind::File,
        libc::S_IFDIR => EntryKind::Directory,
        libc::S_IFLNK => EntryKind::Symlink,
        _ => EntryKind::Other,
    }
}

#[cfg(test)]
#[path = "../tests/privs/stat_in_directory_tests.rs"]
mod tests;
