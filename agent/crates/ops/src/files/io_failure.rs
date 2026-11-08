//! Telling a full quota from every other reason a write failed.

use std::io;

use crate::files::FilesOpError;

/// Classifies a failed create or write: `EDQUOT` is the account's quota, anything
/// else is `otherwise`.
///
/// `ENOSPC` is deliberately not the quota. It means the filesystem is full, which
/// is the host's condition and not something the account can act on, and the
/// free-space floor (issue #148) is what keeps the host from getting there.
#[must_use]
pub(crate) fn io_failure(error: &io::Error, otherwise: FilesOpError) -> FilesOpError {
    if error.raw_os_error() == Some(libc::EDQUOT) {
        FilesOpError::QuotaExceeded
    } else {
        otherwise
    }
}

#[cfg(test)]
#[path = "../tests/files/io_failure_tests.rs"]
mod tests;
