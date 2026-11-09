//! The descriptor walk into the home, run against real directories.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::create_dir_all;
use std::os::unix::fs::symlink;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use super::*;

/// The path every case descends.
fn site() -> RelativePath {
    RelativePath::parse("sites/shop.example.com").unwrap()
}

/// A walk through directories the account owns reaches the directory holding the
/// site — the inverse control for the refusals below.
#[test]
fn a_walk_through_owned_directories_reaches_the_parent() {
    let home = TempDir::new().unwrap();
    create_dir_all(home.path().join("sites")).unwrap();

    let parent = open_parent_of(home.path(), &site(), current_uid().unwrap()).unwrap();

    assert!(parent.metadata().unwrap().is_dir());
}

/// A path of one component is a file directly in the home, and its holder is the
/// home itself.
#[test]
fn a_single_component_is_held_by_the_home_itself() {
    let home = TempDir::new().unwrap();

    let parent = open_parent_of(
        home.path(),
        &RelativePath::parse("public").unwrap(),
        current_uid().unwrap(),
    )
    .unwrap();

    assert!(parent.metadata().unwrap().is_dir());
}

/// A link at the home itself is refused: the one path the walk names is opened
/// without following a link at its last component.
#[test]
fn a_link_standing_in_for_the_home_is_refused() {
    let real = TempDir::new().unwrap();
    let scratch = TempDir::new().unwrap();
    let link = scratch.path().join("alice");
    symlink(real.path(), &link).unwrap();

    let error = open_owned_directory(&link, current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
}

/// A link in the middle of the walk is refused, not followed.
#[test]
fn a_link_in_the_middle_of_the_walk_is_refused() {
    let home = TempDir::new().unwrap();
    let elsewhere = TempDir::new().unwrap();
    symlink(elsewhere.path(), home.path().join("sites")).unwrap();

    let error = open_parent_of(home.path(), &site(), current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
}

/// A level that is a plain file is refused.
#[test]
fn a_plain_file_where_a_directory_belongs_is_refused() {
    let home = TempDir::new().unwrap();
    std::fs::write(home.path().join("sites"), "not a directory").unwrap();

    let error = open_parent_of(home.path(), &site(), current_uid().unwrap()).unwrap_err();

    assert_eq!(error, BackupError::DirectoryUnusable);
}

/// A level owned by somebody else is refused, at the home and below it.
#[test]
fn a_level_owned_by_somebody_else_is_refused() {
    let home = TempDir::new().unwrap();
    create_dir_all(home.path().join("sites")).unwrap();
    let someone_else = current_uid().unwrap() + 1;

    assert_eq!(
        open_owned_directory(home.path(), someone_else).unwrap_err(),
        BackupError::DirectoryUnusable
    );
    assert_eq!(
        open_parent_of(home.path(), &site(), someone_else).unwrap_err(),
        BackupError::DirectoryUnusable
    );
}
