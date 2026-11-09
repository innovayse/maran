//! Tests for [`rename_no_replace_between_directories`], against temporary directories the test owns.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::fs::{self, File, OpenOptions};
use std::os::unix::fs::{OpenOptionsExt, symlink};
use std::path::Path;

use super::rename_no_replace_between_directories;

/// Opens `directory` as a pinned descriptor.
fn pin(directory: &Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(directory)
        .unwrap()
}

#[test]
fn an_entry_moves_between_two_directories() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("a")).unwrap();
    fs::create_dir(dir.path().join("b")).unwrap();
    fs::write(dir.path().join("a/f"), b"x").unwrap();

    rename_no_replace_between_directories(
        &pin(&dir.path().join("a")),
        OsStr::new("f"),
        &pin(&dir.path().join("b")),
        OsStr::new("g"),
    )
    .unwrap();

    assert!(!dir.path().join("a/f").exists());
    assert_eq!(fs::read(dir.path().join("b/g")).unwrap(), b"x");
}

#[test]
fn an_existing_destination_is_refused_and_both_entries_survive() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"new").unwrap();
    fs::write(dir.path().join("g"), b"old").unwrap();
    let held = pin(dir.path());

    let error =
        rename_no_replace_between_directories(&held, OsStr::new("f"), &held, OsStr::new("g"))
            .unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::EEXIST));
    assert_eq!(fs::read(dir.path().join("f")).unwrap(), b"new");
    assert_eq!(fs::read(dir.path().join("g")).unwrap(), b"old");
}

#[test]
fn a_symlink_at_the_destination_is_not_followed_or_replaced() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    fs::write(dir.path().join("target"), b"keep").unwrap();
    symlink("target", dir.path().join("link")).unwrap();
    let held = pin(dir.path());

    let error =
        rename_no_replace_between_directories(&held, OsStr::new("f"), &held, OsStr::new("link"))
            .unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::EEXIST));
    assert_eq!(fs::read(dir.path().join("target")).unwrap(), b"keep");
    assert!(
        fs::symlink_metadata(dir.path().join("link"))
            .unwrap()
            .file_type()
            .is_symlink()
    );
}

#[test]
fn a_missing_source_is_enoent() {
    let dir = tempfile::tempdir().unwrap();
    let held = pin(dir.path());

    let error =
        rename_no_replace_between_directories(&held, OsStr::new("nope"), &held, OsStr::new("g"))
            .unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::ENOENT));
}

#[test]
fn a_name_with_a_separator_is_refused_before_any_syscall() {
    let dir = tempfile::tempdir().unwrap();
    let held = pin(dir.path());

    for (from, to) in [("a/b", "c"), ("a", "../c"), ("", "c")] {
        let error =
            rename_no_replace_between_directories(&held, OsStr::new(from), &held, OsStr::new(to))
                .unwrap_err();
        assert_eq!(error.kind(), std::io::ErrorKind::InvalidInput);
    }
}
