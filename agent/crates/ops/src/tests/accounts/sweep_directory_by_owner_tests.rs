//! Tests for [`sweep`]: what the walk removes, what it must leave, and its bounds.
//!
//! The "account uid" is the test's own uid, which owns everything made in the
//! temporary tree; a uid that matches nothing stands for the foreign owner.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{self, File, OpenOptions};
use std::os::unix::fs::{MetadataExt, OpenOptionsExt, symlink};
use std::path::Path;

use super::{sweep, sweep_directory_by_owner};

/// Opens `path` as a pinned directory descriptor.
fn pin(path: &Path) -> File {
    OpenOptions::new()
        .read(true)
        .custom_flags(libc::O_DIRECTORY | libc::O_NOFOLLOW)
        .open(path)
        .unwrap()
}

/// The uid owning files the test creates.
fn own_uid(path: &Path) -> u32 {
    fs::metadata(path).unwrap().uid()
}

#[test]
fn an_owned_tree_of_files_and_directories_is_removed_and_reported_clean() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir_all(dir.path().join("a/b")).unwrap();
    fs::write(dir.path().join("a/b/f"), b"x").unwrap();
    fs::write(dir.path().join("top"), b"x").unwrap();
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();

    assert!(sweep_directory_by_owner(&root, meta.uid(), meta.dev()).unwrap());
    assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 0);
}

#[test]
fn a_symlink_is_unlinked_and_its_target_outside_the_tree_survives() {
    let dir = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    fs::write(outside.path().join("keep"), b"x").unwrap();
    symlink(outside.path(), dir.path().join("to_dir")).unwrap();
    symlink(outside.path().join("keep"), dir.path().join("to_file")).unwrap();
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();

    assert!(sweep_directory_by_owner(&root, meta.uid(), meta.dev()).unwrap());
    assert_eq!(fs::read_dir(dir.path()).unwrap().count(), 0);
    assert!(outside.path().join("keep").exists());
}

#[test]
fn entries_of_another_uid_are_never_touched() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir(dir.path().join("d")).unwrap();
    fs::write(dir.path().join("d/f"), b"x").unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    symlink("f", dir.path().join("l")).unwrap();
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();
    let stranger = meta.uid().wrapping_add(1);

    assert!(sweep_directory_by_owner(&root, stranger, meta.dev()).unwrap());
    assert!(dir.path().join("d/f").exists());
    assert!(dir.path().join("f").exists());
    assert!(fs::symlink_metadata(dir.path().join("l")).is_ok());
}

#[test]
fn an_entry_on_a_different_device_than_the_root_is_skipped() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();

    // A root device that matches nothing makes every child look like another mount.
    assert!(sweep_directory_by_owner(&root, meta.uid(), meta.dev() + 1).unwrap());
    assert!(dir.path().join("f").exists());
}

#[test]
fn a_tree_deeper_than_the_depth_bound_is_refused_not_reported_clean() {
    let dir = tempfile::tempdir().unwrap();
    fs::create_dir_all(dir.path().join("1/2/3/4")).unwrap();
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();
    let mut budget = 1000;

    let clean = sweep(&root, own_uid(dir.path()), meta.dev(), 0, 2, &mut budget).unwrap();

    assert!(!clean);
    assert!(dir.path().join("1").exists());
}

#[test]
fn exhausting_the_entry_budget_is_refused_not_reported_clean() {
    let dir = tempfile::tempdir().unwrap();
    for n in 0..5 {
        fs::write(dir.path().join(format!("f{n}")), b"x").unwrap();
    }
    let root = pin(dir.path());
    let meta = root.metadata().unwrap();
    let mut budget = 2;

    assert!(!sweep(&root, meta.uid(), meta.dev(), 0, 8, &mut budget).unwrap());
}
