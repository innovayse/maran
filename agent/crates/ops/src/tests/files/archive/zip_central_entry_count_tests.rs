//! Tests for the early reading of a zip's declared entry count.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::Write;

use super::zip_central_entry_count;
use crate::files::archive::archive_fixtures::{ZipMember, set_zip_declared_entries, zip_bytes};

fn on_disk(bytes: &[u8]) -> (tempfile::TempDir, File, u64) {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("a.zip");
    std::fs::File::create(&path)
        .unwrap()
        .write_all(bytes)
        .unwrap();
    let file = File::open(&path).unwrap();
    (directory, file, bytes.len() as u64)
}

#[test]
fn the_count_of_an_ordinary_zip_is_read_from_the_end_record() {
    let bytes = zip_bytes(&[
        ZipMember::file("a.txt", b"one"),
        ZipMember::file("b.txt", b"two"),
        ZipMember::directory("d"),
    ]);
    let (_keep, file, length) = on_disk(&bytes);

    assert_eq!(zip_central_entry_count(&file, length), Some(3));
}

#[test]
fn a_count_the_directory_does_not_hold_is_read_as_declared() {
    let mut bytes = zip_bytes(&[ZipMember::file("a.txt", b"one")]);
    set_zip_declared_entries(&mut bytes, 60_000);
    let (_keep, file, length) = on_disk(&bytes);

    assert_eq!(zip_central_entry_count(&file, length), Some(60_000));
}

#[test]
fn a_file_with_no_end_record_has_no_declared_count() {
    let (_keep, file, length) = on_disk(&vec![0x41_u8; 5000]);

    assert_eq!(zip_central_entry_count(&file, length), None);
}

#[test]
fn a_file_shorter_than_an_end_record_has_no_declared_count() {
    let (_keep, file, length) = on_disk(b"PK");

    assert_eq!(zip_central_entry_count(&file, length), None);
}

#[test]
fn an_end_marker_inside_the_comment_does_not_count_as_the_end_record() {
    let mut bytes = zip_bytes(&[ZipMember::file("a.txt", b"one")]);
    // A bogus record whose comment length does not reach the end of the file,
    // appended after the real one: the real record's comment length no longer
    // lands on the end, and neither does the bogus one.
    bytes.extend_from_slice(&[b'P', b'K', 5, 6]);
    bytes.extend_from_slice(&[0_u8; 16]);
    bytes.extend_from_slice(&[9, 0]);
    bytes.extend_from_slice(b"xx");
    let (_keep, file, length) = on_disk(&bytes);

    assert_eq!(zip_central_entry_count(&file, length), None);
}

#[test]
fn the_zip64_marker_without_a_zip64_record_is_unknown_not_a_guess() {
    let mut bytes = zip_bytes(&[ZipMember::file("a.txt", b"one")]);
    set_zip_declared_entries(&mut bytes, u16::MAX);
    let (_keep, file, length) = on_disk(&bytes);

    assert_eq!(zip_central_entry_count(&file, length), None);
}
