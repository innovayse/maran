//! A full quota is told apart from every other reason a write failed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io;

use super::io_failure;
use crate::files::FilesOpError;

#[test]
fn a_disk_quota_error_is_the_accounts_quota() {
    let error = io::Error::from_raw_os_error(libc::EDQUOT);

    assert_eq!(
        io_failure(&error, FilesOpError::WriteFailed),
        FilesOpError::QuotaExceeded
    );
}

#[test]
fn a_full_filesystem_is_not_the_accounts_quota() {
    let error = io::Error::from_raw_os_error(libc::ENOSPC);

    assert_eq!(
        io_failure(&error, FilesOpError::WriteFailed),
        FilesOpError::WriteFailed
    );
}

#[test]
fn any_other_error_keeps_the_fallback_it_was_given() {
    let error = io::Error::from_raw_os_error(libc::EIO);

    assert_eq!(
        io_failure(&error, FilesOpError::MutationFailed),
        FilesOpError::MutationFailed
    );
}
