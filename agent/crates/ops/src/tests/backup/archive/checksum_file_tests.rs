//! The digest is SHA-256 over the whole file, or it is a failure.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::write;

use tempfile::TempDir;

use super::*;

/// **A positive control.** A known string hashes to its published SHA-256, so
/// this test can tell a correct digest from any other number.
#[test]
fn a_known_string_hashes_to_its_published_digest() {
    let directory = TempDir::new().expect("a temporary directory");
    let file = directory.path().join("abc");
    write(&file, b"abc").expect("the fixture file");

    let (digest, bytes) = checksum_file(&file).expect("the file is readable");

    assert_eq!(
        digest,
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    );
    assert_eq!(bytes, 3);
}

/// An empty file hashes to the empty digest rather than to nothing.
#[test]
fn an_empty_file_hashes_to_the_empty_digest() {
    let directory = TempDir::new().expect("a temporary directory");
    let file = directory.path().join("empty");
    write(&file, b"").expect("the fixture file");

    let (digest, bytes) = checksum_file(&file).expect("the file is readable");

    assert_eq!(
        digest,
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    );
    assert_eq!(bytes, 0);
}

/// A file larger than one read chunk is hashed whole, not to its first chunk.
#[test]
fn a_file_larger_than_one_chunk_is_hashed_whole() {
    let directory = TempDir::new().expect("a temporary directory");
    let short = directory.path().join("short");
    let long = directory.path().join("long");
    write(&short, vec![b'x'; CHUNK_BYTES]).expect("the fixture file");
    write(&long, vec![b'x'; CHUNK_BYTES + 1]).expect("the fixture file");

    let (short_digest, short_bytes) = checksum_file(&short).expect("readable");
    let (long_digest, long_bytes) = checksum_file(&long).expect("readable");

    assert_ne!(short_digest, long_digest);
    assert_eq!(short_bytes as usize, CHUNK_BYTES);
    assert_eq!(long_bytes as usize, CHUNK_BYTES + 1);
}

/// A file that is not there has no digest, and is not treated as an empty one.
#[test]
fn a_missing_file_is_reported_rather_than_hashed_as_empty() {
    let directory = TempDir::new().expect("a temporary directory");

    let refusal = checksum_file(&directory.path().join("absent"));

    assert!(matches!(refusal, Err(BackupError::ChecksumUnreadable)));
}
