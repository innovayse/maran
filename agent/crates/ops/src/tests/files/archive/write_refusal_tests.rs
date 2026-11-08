//! Tests for the reading of a failed write.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Error;

use super::write_refusal;
use crate::files::archive::archive_refusal::ArchiveRefusal;

#[test]
fn a_quota_error_is_a_quota_refusal() {
    assert_eq!(
        write_refusal(&Error::from_raw_os_error(libc::EDQUOT)),
        ArchiveRefusal::Quota
    );
}

#[test]
fn a_full_filesystem_is_a_write_failure_and_not_the_accounts_quota() {
    assert_eq!(
        write_refusal(&Error::from_raw_os_error(libc::ENOSPC)),
        ArchiveRefusal::WriteFailed
    );
}

#[test]
fn any_other_error_is_a_write_failure() {
    assert_eq!(
        write_refusal(&Error::from_raw_os_error(libc::EIO)),
        ArchiveRefusal::WriteFailed
    );
    assert_eq!(
        write_refusal(&Error::other("x")),
        ArchiveRefusal::WriteFailed
    );
}
