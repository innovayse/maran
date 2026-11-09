//! Reading a refused rename.

use std::io;

use crate::files::FilesOpError;

/// Maps the operating system's answer to a no-replace rename onto this area's vocabulary.
///
/// `EEXIST` and `ENOTEMPTY` are a destination that is taken, `ENOENT` a source
/// that is gone, `EXDEV` a destination on another filesystem (there is no
/// copy-and-delete fallback, so the move simply did not happen) and `EDQUOT` the
/// account's quota. `EINVAL` is not named: the destination-inside-source case is
/// decided lexically before the rename, so a kernel `EINVAL` here means the
/// filesystem does not support a rename that refuses to replace, and that is a
/// plain failure rather than a verdict about the request.
#[must_use]
pub(crate) fn rename_failure(error: &io::Error) -> FilesOpError {
    match error.raw_os_error() {
        Some(libc::EEXIST | libc::ENOTEMPTY) => FilesOpError::AlreadyExists,
        Some(libc::ENOENT) => FilesOpError::NotFound,
        Some(libc::EXDEV) => FilesOpError::CrossesDevice,
        Some(libc::EDQUOT) => FilesOpError::QuotaExceeded,
        _ => FilesOpError::MutationFailed,
    }
}

#[cfg(test)]
#[path = "../tests/files/rename_failure_tests.rs"]
mod tests;
