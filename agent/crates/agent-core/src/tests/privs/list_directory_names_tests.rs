//! Tests for [`list_directory_names`], against a temporary directory the test owns.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::{OsStr, OsString};
use std::fs::{self, File, OpenOptions};
use std::os::unix::ffi::OsStrExt;
use std::os::unix::fs::OpenOptionsExt;

use super::list_directory_names;

/// Opens `directory` as a pinned descriptor.
fn pin(directory: &std::path::Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(directory)
        .unwrap()
}

/// Makes `count` empty files called `f0`, `f1`, ...
fn fill(directory: &std::path::Path, count: usize) {
    for index in 0..count {
        fs::write(directory.join(format!("f{index}")), b"").unwrap();
    }
}

#[test]
fn every_name_is_listed_without_the_directorys_own_links() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("a"), b"x").unwrap();
    fs::create_dir(dir.path().join("b")).unwrap();
    std::os::unix::fs::symlink("a", dir.path().join("c")).unwrap();

    let read = list_directory_names(&pin(dir.path()), 100).unwrap();

    let mut names = read.names;
    names.sort();
    assert_eq!(
        names,
        vec![
            OsString::from("a"),
            OsString::from("b"),
            OsString::from("c")
        ]
    );
    assert!(!read.exceeded);
}

#[test]
fn an_empty_directory_lists_nothing_and_is_not_exceeded() {
    let dir = tempfile::tempdir().unwrap();

    let read = list_directory_names(&pin(dir.path()), 10).unwrap();

    assert!(read.names.is_empty());
    assert!(!read.exceeded);
}

#[test]
fn a_directory_over_the_bound_is_cut_at_the_bound_and_flagged() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 5);

    let read = list_directory_names(&pin(dir.path()), 3).unwrap();

    assert_eq!(read.names.len(), 3);
    assert!(read.exceeded);
}

#[test]
fn a_directory_one_name_under_the_bound_is_whole_and_not_flagged() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 4);

    let read = list_directory_names(&pin(dir.path()), 5).unwrap();

    assert_eq!(read.names.len(), 4);
    assert!(!read.exceeded);
}

#[test]
fn a_directory_exactly_at_the_bound_is_flagged_so_limit_plus_one_detects_the_limit() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 4);

    let read = list_directory_names(&pin(dir.path()), 4).unwrap();

    assert_eq!(read.names.len(), 4);
    assert!(read.exceeded);
}

#[test]
fn a_zero_bound_reads_nothing_and_is_flagged() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 2);

    let read = list_directory_names(&pin(dir.path()), 0).unwrap();

    assert!(read.names.is_empty());
    assert!(read.exceeded);
}

#[test]
fn a_second_read_on_the_same_descriptor_starts_from_the_beginning() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 3);
    let pinned = pin(dir.path());

    let first = list_directory_names(&pinned, 100).unwrap();
    let second = list_directory_names(&pinned, 100).unwrap();

    assert_eq!(first.names.len(), 3);
    assert_eq!(second.names.len(), 3);
}

#[test]
fn the_callers_descriptor_stays_usable_after_the_read() {
    let dir = tempfile::tempdir().unwrap();
    fill(dir.path(), 1);
    let pinned = pin(dir.path());

    list_directory_names(&pinned, 100).unwrap();

    let stat =
        crate::privs::stat_in_directory::stat_in_directory(&pinned, OsStr::new("f0")).unwrap();
    assert_eq!(stat.links, 1);
}

#[test]
fn a_name_that_is_not_utf8_is_returned_as_its_raw_bytes() {
    let dir = tempfile::tempdir().unwrap();
    let raw = OsStr::from_bytes(b"bad\xff\xfename");
    fs::write(dir.path().join(raw), b"").unwrap();

    let read = list_directory_names(&pin(dir.path()), 10).unwrap();

    assert_eq!(read.names, vec![raw.to_owned()]);
}

#[test]
fn a_file_is_not_a_directory_and_is_refused() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    let file = File::open(dir.path().join("f")).unwrap();

    let error = list_directory_names(&file, 10).unwrap_err();

    assert_eq!(error.raw_os_error(), Some(libc::ENOTDIR));
}
