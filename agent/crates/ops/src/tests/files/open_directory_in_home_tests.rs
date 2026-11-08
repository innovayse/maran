//! Tests for the descriptor walk to a whole browse path.
//!
//! None of these needs root: a test owns a `tempfile::TempDir` and runs as its
//! own uid, the same relationship a hosting customer has to their home. The
//! walk takes the home and the uid as arguments for exactly that reason.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, symlink};

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::browse_path::BrowsePath;

use super::open_directory_in_home;
use crate::files::FilesOpError;

#[test]
fn the_empty_path_opens_the_home_itself() {
    let home = tempfile::tempdir().unwrap();

    let opened = open_directory_in_home(
        home.path(),
        &BrowsePath::parse("").unwrap(),
        current_uid().unwrap(),
    )
    .unwrap();

    assert_eq!(
        opened.metadata().unwrap().ino(),
        fs::metadata(home.path()).unwrap().ino()
    );
}

#[test]
fn a_nested_directory_is_opened_level_by_level() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("sites/example.com")).unwrap();

    let opened = open_directory_in_home(
        home.path(),
        &BrowsePath::parse("sites/example.com").unwrap(),
        current_uid().unwrap(),
    )
    .unwrap();

    assert_eq!(
        opened.metadata().unwrap().ino(),
        fs::metadata(home.path().join("sites/example.com"))
            .unwrap()
            .ino()
    );
}

#[test]
fn a_missing_directory_is_refused_and_not_created() {
    let home = tempfile::tempdir().unwrap();

    let result = open_directory_in_home(
        home.path(),
        &BrowsePath::parse("nothing/here").unwrap(),
        current_uid().unwrap(),
    );

    assert_eq!(result.unwrap_err(), FilesOpError::DirectoryUnusable);
    assert!(!home.path().join("nothing").exists());
}

#[test]
fn a_symlinked_component_is_refused_even_when_it_points_at_a_directory() {
    let home = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    fs::create_dir(outside.path().join("inner")).unwrap();
    symlink(outside.path(), home.path().join("escape")).unwrap();
    // Positive control: the same walk through a real directory succeeds.
    fs::create_dir_all(home.path().join("real/inner")).unwrap();
    let uid = current_uid().unwrap();

    assert!(
        open_directory_in_home(home.path(), &BrowsePath::parse("real/inner").unwrap(), uid).is_ok()
    );
    assert_eq!(
        open_directory_in_home(
            home.path(),
            &BrowsePath::parse("escape/inner").unwrap(),
            uid
        )
        .unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
    assert_eq!(
        open_directory_in_home(home.path(), &BrowsePath::parse("escape").unwrap(), uid)
            .unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
}

#[test]
fn a_plain_file_where_a_directory_is_named_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("file.txt"), b"x").unwrap();

    let result = open_directory_in_home(
        home.path(),
        &BrowsePath::parse("file.txt").unwrap(),
        current_uid().unwrap(),
    );

    assert_eq!(result.unwrap_err(), FilesOpError::DirectoryUnusable);
}

#[test]
fn a_home_that_belongs_to_somebody_else_is_refused() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("sub")).unwrap();
    let not_the_owner = current_uid().unwrap() + 1;

    // The home itself is the first thing checked, so the walk stops there.
    let result = open_directory_in_home(
        home.path(),
        &BrowsePath::parse("sub").unwrap(),
        not_the_owner,
    );

    assert_eq!(result.unwrap_err(), FilesOpError::HomeUnusable);
}

#[test]
fn a_missing_home_is_refused_as_an_unusable_home() {
    let home = tempfile::tempdir().unwrap();

    let result = open_directory_in_home(
        &home.path().join("gone"),
        &BrowsePath::parse("").unwrap(),
        current_uid().unwrap(),
    );

    assert_eq!(result.unwrap_err(), FilesOpError::HomeUnusable);
}
