//! A file says it is sealed by its own first bytes.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::write;

use tempfile::TempDir;

use super::*;

#[test]
fn a_file_beginning_with_the_magic_is_sealed() {
    let directory = TempDir::new().unwrap();
    let file = directory.path().join("a");
    write(&file, b"MARANENC\x01and the rest").unwrap();

    assert!(is_encrypted_artifact(&file).unwrap());
}

#[test]
fn a_gzip_stream_is_not_sealed() {
    let directory = TempDir::new().unwrap();
    let file = directory.path().join("a");
    write(
        &file,
        [0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03],
    )
    .unwrap();

    assert!(!is_encrypted_artifact(&file).unwrap());
}

#[test]
fn an_empty_file_and_one_shorter_than_the_magic_are_not_sealed() {
    let directory = TempDir::new().unwrap();
    let empty = directory.path().join("empty");
    let short = directory.path().join("short");
    write(&empty, b"").unwrap();
    write(&short, b"MARAN").unwrap();

    assert!(!is_encrypted_artifact(&empty).unwrap());
    assert!(!is_encrypted_artifact(&short).unwrap());
}

#[test]
fn a_file_that_does_not_exist_is_unreadable_and_not_plain() {
    let directory = TempDir::new().unwrap();

    let refusal = is_encrypted_artifact(&directory.path().join("absent"));

    assert_eq!(refusal, Err(BackupError::ChecksumUnreadable));
}
