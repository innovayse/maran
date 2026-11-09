//! Reading the operating system's refusal of a write.

use std::io;

use super::archive_refusal::ArchiveRefusal;

/// The refusal for a failed write, rename, `mkdir` or permission change.
///
/// `EDQUOT` is the account's quota and is its own refusal, so the panel can say
/// so; `ENOSPC` is a full filesystem, the HOST's problem, and stays a plain
/// write failure. Everything else is a write failure too.
pub(crate) fn write_refusal(error: &io::Error) -> ArchiveRefusal {
    if error.raw_os_error() == Some(libc::EDQUOT) {
        ArchiveRefusal::Quota
    } else {
        ArchiveRefusal::WriteFailed
    }
}

#[cfg(test)]
#[path = "../../tests/files/archive/write_refusal_tests.rs"]
mod tests;
