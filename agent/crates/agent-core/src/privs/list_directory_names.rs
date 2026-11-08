//! Reading the entry names of a directory already held open, with a bound.

use std::ffi::OsString;
use std::fs::File;
use std::io;
use std::os::unix::ffi::OsStringExt;
use std::os::unix::io::AsRawFd;

use super::directory_names::DirectoryNames;

/// Owns a `DIR*` and closes it, descriptor included, on every path out.
struct OpenStream(*mut libc::DIR);

impl Drop for OpenStream {
    fn drop(&mut self) {
        // SAFETY: `self.0` came from a successful `fdopendir` and is closed only
        // here, once. `closedir` also closes the descriptor `fdopendir` took.
        unsafe {
            libc::closedir(self.0);
        }
    }
}

/// Reads at most `max` entry names from `directory`, excluding `.` and `..`.
///
/// The caller's descriptor is not consumed or moved: the stream is built on a
/// duplicate (`F_DUPFD_CLOEXEC`), so the caller can keep using `directory` for
/// `stat_in_directory` and `open_in_directory` afterwards, and the read always
/// starts from the beginning (`rewinddir`), whatever an earlier read left.
/// Nothing is opened by NAME: the listing cannot be steered anywhere by what the
/// names contain, and no entry is ever opened or followed.
///
/// The read is bounded in memory: it stops as soon as `max` names are collected
/// and reports [`DirectoryNames::exceeded`], so a customer's directory of
/// millions of entries costs the bound and not the directory. `max` of zero
/// reads nothing and reports exceeded.
///
/// # Errors
///
/// Returns the operating system's error when the descriptor cannot be
/// duplicated, is not a directory (`ENOTDIR`), or a read fails part-way. A
/// partial result is never returned: a failed read is an error, not a short list.
pub fn list_directory_names(directory: &File, max: usize) -> io::Result<DirectoryNames> {
    // SAFETY: `directory` is a live `File`, so its descriptor is valid for the
    // call; `F_DUPFD_CLOEXEC` takes an integer argument and returns a new
    // descriptor the stream below takes ownership of.
    let duplicate = unsafe { libc::fcntl(directory.as_raw_fd(), libc::F_DUPFD_CLOEXEC, 0) };
    if duplicate < 0 {
        return Err(io::Error::last_os_error());
    }

    // SAFETY: `duplicate` is a descriptor this function owns. On success
    // `fdopendir` takes ownership of it (closed by `closedir`); on failure it
    // stays ours and is closed here.
    let stream = unsafe { libc::fdopendir(duplicate) };
    if stream.is_null() {
        let error = io::Error::last_os_error();
        // SAFETY: `fdopendir` failed, so `duplicate` is still open and ours.
        unsafe {
            libc::close(duplicate);
        }
        return Err(error);
    }
    let stream = OpenStream(stream);

    // SAFETY: `stream.0` is a live `DIR*`. The duplicate shares its file offset
    // with `directory`, so this also rewinds the caller's descriptor, which is
    // what makes the read independent of any earlier one.
    unsafe { libc::rewinddir(stream.0) };

    let mut names = Vec::new();
    loop {
        if names.len() >= max {
            return Ok(DirectoryNames {
                names,
                exceeded: true,
            });
        }

        // SAFETY: `__errno_location` returns the calling thread's errno slot;
        // clearing it is how `readdir` distinguishes the end of the directory
        // from a failure, since both return NULL.
        unsafe { *libc::__errno_location() = 0 };
        // SAFETY: `stream.0` is a live `DIR*` used by this thread alone.
        let entry = unsafe { libc::readdir(stream.0) };
        if entry.is_null() {
            let errno = io::Error::last_os_error();
            return match errno.raw_os_error() {
                Some(0) | None => Ok(DirectoryNames {
                    names,
                    exceeded: false,
                }),
                Some(_) => Err(errno),
            };
        }

        // SAFETY: `readdir` returned a non-null pointer to a `dirent` whose
        // `d_name` is NUL-terminated; it is copied before the next `readdir`.
        let bytes = unsafe { std::ffi::CStr::from_ptr((*entry).d_name.as_ptr()) }.to_bytes();
        if bytes == b"." || bytes == b".." {
            continue;
        }
        names.push(OsString::from_vec(bytes.to_vec()));
    }
}

#[cfg(test)]
#[path = "../tests/privs/list_directory_names_tests.rs"]
mod tests;
