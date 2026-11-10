//! Tests for [`TransferScratch`]: the directory is removed whatever happens next.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use super::TransferScratch;
use crate::transfer::fake_transfer_host::FakeTransferHost;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Fails after creating the scratch, to drive the early-return path.
fn create_then_fail(host: &FakeTransferHost) -> Result<(), TransferOpError> {
    let scratch = TransferScratch::create(host)?;
    host.write_private_file(&scratch.file("key"), b"secret")?;

    Err(TransferOpError::KeyGenerationFailed)
}

#[test]
fn dropping_the_guard_removes_the_directory_and_its_files() {
    let host = FakeTransferHost::new();
    {
        let scratch = TransferScratch::create(&host).unwrap();
        host.write_private_file(&scratch.file("key"), b"secret")
            .unwrap();
        assert_eq!(host.files().len(), 1);
        assert!(
            host.removed().is_empty(),
            "removed before the guard dropped"
        );
    }

    assert_eq!(host.removed(), host.created());
    assert!(host.files().is_empty());
}

#[test]
fn an_early_error_return_still_removes_the_directory() {
    let host = FakeTransferHost::new();

    assert!(create_then_fail(&host).is_err());

    assert_eq!(host.removed(), host.created());
    assert!(host.files().is_empty());
}

#[test]
fn a_file_path_is_inside_the_directory() {
    let host = FakeTransferHost::new();
    let scratch = TransferScratch::create(&host).unwrap();
    let file: PathBuf = scratch.file("known_hosts");

    assert_eq!(file.parent(), Some(scratch.path()));
    assert_eq!(file.file_name().unwrap(), "known_hosts");
}
