//! Sealing the `.partial` in place: one name, sealed bytes, no plain copy left.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{read, read_dir, write};

use tempfile::TempDir;

use crate::backup::archive::decrypt_artifact::decrypt_artifact;

use super::*;

/// A key a test can name.
fn key(byte: &str) -> ArchiveKey {
    ArchiveKey::parse(&byte.repeat(32)).expect("the fixture key is well-formed")
}

#[test]
fn the_partial_becomes_a_sealed_file_under_the_same_name_and_nothing_else_is_left() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("b.tar.gz.partial");
    write(&partial, b"the plain archive bytes").unwrap();

    let bytes = seal_partial(&partial, &key("11")).unwrap();

    let stored = read(&partial).unwrap();
    assert!(stored.starts_with(b"MARANENC"));
    assert_eq!(stored.len() as u64, bytes);
    let names: Vec<_> = read_dir(directory.path())
        .unwrap()
        .map(|entry| entry.unwrap().file_name())
        .collect();
    assert_eq!(names.len(), 1, "the sealing copy must be gone: {names:?}");
}

#[test]
fn what_was_sealed_opens_back_to_the_plain_archive() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("b.tar.gz.partial");
    write(&partial, b"the plain archive bytes").unwrap();
    seal_partial(&partial, &key("11")).unwrap();

    decrypt_artifact(&partial, &directory.path().join("opened"), &key("11")).unwrap();

    assert_eq!(
        read(directory.path().join("opened")).unwrap(),
        b"the plain archive bytes"
    );
}

#[test]
fn a_leftover_sealing_copy_from_a_run_that_died_is_replaced() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("b.tar.gz.partial");
    write(&partial, b"plain").unwrap();
    write(directory.path().join("b.tar.gz.partial.sealing"), b"litter").unwrap();

    seal_partial(&partial, &key("11")).unwrap();

    assert!(read(&partial).unwrap().starts_with(b"MARANENC"));
    assert!(!directory.path().join("b.tar.gz.partial.sealing").exists());
}

#[test]
fn a_partial_that_is_not_there_fails_and_leaves_no_sealing_copy() {
    let directory = TempDir::new().unwrap();
    let partial = directory.path().join("b.tar.gz.partial");

    let refusal = seal_partial(&partial, &key("11"));

    assert_eq!(refusal, Err(BackupError::ArtifactEncryptionFailed));
    assert!(!directory.path().join("b.tar.gz.partial.sealing").exists());
    assert!(!partial.exists());
}
