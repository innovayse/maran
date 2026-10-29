//! Tests for [`sweep_roots`]: the roots are swept, verified, and a missing one skipped.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, symlink};

use super::sweep_roots;
use crate::accounts::AccountError;

#[test]
fn owned_files_in_every_root_are_removed() {
    let first = tempfile::tempdir().unwrap();
    let second = tempfile::tempdir().unwrap();
    fs::write(first.path().join("f"), b"x").unwrap();
    fs::create_dir(second.path().join("d")).unwrap();
    fs::write(second.path().join("d/g"), b"x").unwrap();
    let uid = fs::metadata(first.path()).unwrap().uid();
    let roots = [
        first.path().to_str().unwrap(),
        second.path().to_str().unwrap(),
    ];

    sweep_roots(&roots, "acct", uid).unwrap();

    assert_eq!(fs::read_dir(first.path()).unwrap().count(), 0);
    assert_eq!(fs::read_dir(second.path()).unwrap().count(), 0);
}

#[test]
fn a_root_that_does_not_exist_is_skipped() {
    let dir = tempfile::tempdir().unwrap();
    let missing = dir.path().join("absent");
    let roots = [missing.to_str().unwrap()];

    assert!(sweep_roots(&roots, "acct", 4242).is_ok());
}

#[test]
fn files_of_another_uid_survive_and_are_not_a_refusal() {
    let dir = tempfile::tempdir().unwrap();
    fs::write(dir.path().join("f"), b"x").unwrap();
    let stranger = fs::metadata(dir.path()).unwrap().uid().wrapping_add(1);
    let roots = [dir.path().to_str().unwrap()];

    sweep_roots(&roots, "acct", stranger).unwrap();

    assert!(dir.path().join("f").exists());
}

#[test]
fn a_root_that_is_a_symlink_is_refused_not_followed() {
    let target = tempfile::tempdir().unwrap();
    fs::write(target.path().join("f"), b"x").unwrap();
    let holder = tempfile::tempdir().unwrap();
    let link = holder.path().join("root");
    symlink(target.path(), &link).unwrap();
    let uid = fs::metadata(target.path()).unwrap().uid();
    let roots = [link.to_str().unwrap()];

    let error = sweep_roots(&roots, "acct", uid).unwrap_err();

    assert!(matches!(error, AccountError::HomeInspection { .. }));
    assert!(target.path().join("f").exists());
}

#[test]
fn a_tree_the_sweep_cannot_finish_is_reported_as_remaining() {
    // 70 nested directories exceed the depth bound, so the verification pass
    // cannot report the tree clean.
    let dir = tempfile::tempdir().unwrap();
    let mut path = dir.path().to_path_buf();
    for _ in 0..70 {
        path.push("d");
    }
    fs::create_dir_all(&path).unwrap();
    let uid = fs::metadata(dir.path()).unwrap().uid();
    let roots = [dir.path().to_str().unwrap()];

    let error = sweep_roots(&roots, "acct", uid).unwrap_err();

    assert!(matches!(error, AccountError::SharedFilesRemain { .. }));
}
