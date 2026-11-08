//! Each answer of a refused no-replace rename becomes the verdict it stands for.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io;

use super::rename_failure;
use crate::files::FilesOpError;

fn classified(errno: i32) -> FilesOpError {
    rename_failure(&io::Error::from_raw_os_error(errno))
}

#[test]
fn a_taken_destination_is_already_exists() {
    assert_eq!(classified(libc::EEXIST), FilesOpError::AlreadyExists);
    assert_eq!(classified(libc::ENOTEMPTY), FilesOpError::AlreadyExists);
}

#[test]
fn a_vanished_source_is_not_found() {
    assert_eq!(classified(libc::ENOENT), FilesOpError::NotFound);
}

#[test]
fn another_filesystem_is_crosses_device_and_never_a_fallback() {
    assert_eq!(classified(libc::EXDEV), FilesOpError::CrossesDevice);
}

#[test]
fn a_disk_quota_error_is_the_accounts_quota() {
    assert_eq!(classified(libc::EDQUOT), FilesOpError::QuotaExceeded);
}

#[test]
fn an_unsupported_rename_is_a_plain_failure_and_not_a_verdict_about_the_request() {
    assert_eq!(classified(libc::EINVAL), FilesOpError::MutationFailed);
}
