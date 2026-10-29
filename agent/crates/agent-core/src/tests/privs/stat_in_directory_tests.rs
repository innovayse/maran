//! Tests for [`stat_in_directory`], against a temporary directory the test owns.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::fs::{self, File, OpenOptions};
use std::io;
use std::os::unix::fs::{MetadataExt, OpenOptionsExt, symlink};

use super::stat_in_directory;
use crate::privs::entry_kind::EntryKind;

/// Opens `directory` as a pinned descriptor.
fn pin(directory: &std::path::Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(directory)
        .unwrap()
}

#[test]
fn a_regular_file_is_reported_with_its_owner_and_one_link() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    let meta = fs::metadata(dir.path().join("f")).unwrap();

    let stat = stat_in_directory(&pin(dir.path()), OsStr::new("f")).unwrap();

    assert_eq!(stat.kind, EntryKind::File);
    assert_eq!(stat.uid, meta.uid());
    assert_eq!(stat.gid, meta.gid());
    assert_eq!(stat.device, meta.dev());
    assert_eq!(stat.links, 1);
}

#[test]
fn a_directory_is_reported_as_a_directory() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("d")).unwrap();

    let stat = stat_in_directory(&pin(dir.path()), OsStr::new("d")).unwrap();

    assert_eq!(stat.kind, EntryKind::Directory);
}

#[test]
fn a_symlink_is_reported_as_a_symlink_and_not_followed() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("target"), b"x").unwrap();
    fs::create_dir(dir.path().join("target_dir")).unwrap();
    symlink("target", dir.path().join("to_file")).unwrap();
    symlink("target_dir", dir.path().join("to_dir")).unwrap();
    symlink("missing", dir.path().join("dangling")).unwrap();
    let pinned = pin(dir.path());

    for name in ["to_file", "to_dir", "dangling"] {
        let stat = stat_in_directory(&pinned, OsStr::new(name)).unwrap();
        assert_eq!(stat.kind, EntryKind::Symlink, "{name}");
    }
}

#[test]
fn a_fifo_is_reported_as_other() {
    let dir = tempfile::tempdir().unwrap();
    let path = std::ffi::CString::new(dir.path().join("p").to_str().unwrap()).unwrap();
    // SAFETY: `path` is a valid NUL-terminated string that outlives the call.
    assert_eq!(unsafe { libc::mkfifo(path.as_ptr(), 0o600) }, 0);

    let stat = stat_in_directory(&pin(dir.path()), OsStr::new("p")).unwrap();

    assert_eq!(stat.kind, EntryKind::Other);
}

#[test]
fn a_hard_link_raises_the_link_count() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("a"), b"x").unwrap();
    fs::hard_link(dir.path().join("a"), dir.path().join("b")).unwrap();

    let stat = stat_in_directory(&pin(dir.path()), OsStr::new("a")).unwrap();

    assert_eq!(stat.links, 2);
}

#[test]
fn a_missing_name_is_not_found() {
    let dir = tempfile::tempdir().unwrap();

    let error = stat_in_directory(&pin(dir.path()), OsStr::new("nope")).unwrap_err();

    assert_eq!(error.kind(), io::ErrorKind::NotFound);
}

#[test]
fn a_name_that_would_leave_the_directory_is_refused_before_the_syscall() {
    let dir = tempfile::tempdir().unwrap();
    let pinned = pin(dir.path());

    for name in ["..", ".", "a/b", "/etc/passwd", ""] {
        let error = stat_in_directory(&pinned, OsStr::new(name)).unwrap_err();
        assert_eq!(error.kind(), io::ErrorKind::InvalidInput, "{name:?}");
    }
}
