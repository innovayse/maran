//! Tests for [`remove_directory_in_directory`], against a temporary directory the test owns.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::fs::{self, File, OpenOptions};
use std::io;
use std::os::unix::fs::{OpenOptionsExt, symlink};

use super::remove_directory_in_directory;

/// Opens `directory` as a pinned descriptor.
fn pin(directory: &std::path::Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(directory)
        .unwrap()
}

#[test]
fn an_empty_directory_is_removed() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("d")).unwrap();

    remove_directory_in_directory(&pin(dir.path()), OsStr::new("d")).unwrap();

    assert!(!dir.path().join("d").exists());
}

#[test]
fn a_non_empty_directory_is_refused_and_left_intact() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("d")).unwrap();
    fs::write(dir.path().join("d/f"), b"x").unwrap();

    let error = remove_directory_in_directory(&pin(dir.path()), OsStr::new("d")).unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::ENOTEMPTY));
    assert!(dir.path().join("d/f").exists());
}

#[test]
fn a_regular_file_is_refused() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();

    let error = remove_directory_in_directory(&pin(dir.path()), OsStr::new("f")).unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::ENOTDIR));
    assert!(dir.path().join("f").exists());
}

#[test]
fn a_symlink_to_an_empty_directory_is_not_followed_or_removed() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("real")).unwrap();
    symlink("real", dir.path().join("link")).unwrap();

    let error = remove_directory_in_directory(&pin(dir.path()), OsStr::new("link")).unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::ENOTDIR));
    assert!(dir.path().join("real").is_dir());
    assert!(fs::symlink_metadata(dir.path().join("link")).is_ok());
}

#[test]
fn a_missing_name_is_not_found() {
    let dir = tempfile::tempdir().unwrap();

    let error = remove_directory_in_directory(&pin(dir.path()), OsStr::new("nope")).unwrap_err();

    assert_eq!(error.kind(), io::ErrorKind::NotFound);
}

#[test]
fn a_name_that_would_leave_the_directory_is_refused_before_the_syscall() {
    let dir = tempfile::tempdir().unwrap();
    let pinned = pin(dir.path());

    for name in ["..", ".", "a/b", "/tmp", ""] {
        let error = remove_directory_in_directory(&pinned, OsStr::new(name)).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::InvalidInput, "{name:?}");
    }
}
