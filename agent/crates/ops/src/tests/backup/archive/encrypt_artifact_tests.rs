//! What a sealed archive looks like on disk, and what it must not show.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{read, write};
use std::os::unix::fs::PermissionsExt as _;

use tempfile::TempDir;

use crate::backup::archive::encrypted_artifact_header::{HEADER_BYTES, TAG_BYTES};

use super::*;

/// A key a test can name.
fn key(byte: &str) -> ArchiveKey {
    ArchiveKey::parse(&byte.repeat(32)).expect("the fixture key is well-formed")
}

/// `length` bytes that are not all alike, so a chunk boundary moved by a byte
/// changes what is read.
fn bytes_of(length: usize) -> Vec<u8> {
    (0..length).map(|index| (index % 251) as u8).collect()
}

#[test]
fn a_sealed_file_begins_with_the_header_and_never_contains_the_plain_bytes() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    let sealed = directory.path().join("sealed");
    let secret = b"customer-database-password-12345".repeat(100);
    write(&plain, &secret).unwrap();

    let written = encrypt_artifact(&plain, &sealed, &key("11")).unwrap();

    let stored = read(&sealed).unwrap();
    assert_eq!(written as usize, stored.len());
    assert!(stored.starts_with(b"MARANENC\x01"));
    assert!(
        !stored.windows(12).any(|window| window == b"database-pas"),
        "a run of the plain text is visible in the sealed file"
    );
    assert!(
        stored.len() > secret.len(),
        "the sealed file adds a header, a length and a tag"
    );
}

#[test]
fn a_sealed_file_is_root_only_from_its_first_byte() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    let sealed = directory.path().join("sealed");
    write(&plain, b"x").unwrap();

    encrypt_artifact(&plain, &sealed, &key("11")).unwrap();

    let mode = std::fs::metadata(&sealed).unwrap().permissions().mode() & 0o7777;
    assert_eq!(mode, 0o600);
}

#[test]
fn the_same_plain_file_sealed_twice_gives_different_bytes() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    write(&plain, bytes_of(5000)).unwrap();

    encrypt_artifact(&plain, &directory.path().join("one"), &key("11")).unwrap();
    encrypt_artifact(&plain, &directory.path().join("two"), &key("11")).unwrap();

    assert_ne!(
        read(directory.path().join("one")).unwrap(),
        read(directory.path().join("two")).unwrap(),
        "a repeated nonce prefix would make two archives of one file identical"
    );
}

#[test]
fn an_existing_target_is_refused_and_left_alone() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    let sealed = directory.path().join("sealed");
    write(&plain, b"x").unwrap();
    write(&sealed, b"somebody else's file").unwrap();

    let refusal = encrypt_artifact(&plain, &sealed, &key("11"));

    assert_eq!(refusal, Err(BackupError::ArtifactEncryptionFailed));
    assert_eq!(read(&sealed).unwrap(), b"somebody else's file");
}

#[test]
fn a_plain_file_that_is_not_there_is_an_encryption_failure() {
    let directory = TempDir::new().unwrap();

    let refusal = encrypt_artifact(
        &directory.path().join("absent"),
        &directory.path().join("sealed"),
        &key("11"),
    );

    assert_eq!(refusal, Err(BackupError::ArtifactEncryptionFailed));
}

#[test]
fn an_empty_plain_file_still_produces_one_closing_frame() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    let sealed = directory.path().join("sealed");
    write(&plain, b"").unwrap();

    let written = encrypt_artifact(&plain, &sealed, &key("11")).unwrap();

    // header + a four-byte length + a sixteen-byte tag over nothing.
    assert_eq!(written as usize, HEADER_BYTES + 4 + TAG_BYTES);
}

#[test]
fn a_file_larger_than_one_chunk_is_framed_in_several() {
    let directory = TempDir::new().unwrap();
    let plain = directory.path().join("plain");
    let sealed = directory.path().join("sealed");
    let length = CHUNK_BYTES * 2 + 17;
    write(&plain, bytes_of(length)).unwrap();

    let written = encrypt_artifact(&plain, &sealed, &key("11")).unwrap() as usize;

    // three frames, each with its length and tag.
    assert_eq!(written, HEADER_BYTES + length + 3 * (4 + TAG_BYTES));
}
