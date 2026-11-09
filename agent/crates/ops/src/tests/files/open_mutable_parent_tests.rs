//! The descent to the directory holding a mutable path's final name.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::MetadataExt;

use super::open_mutable_parent;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};
use crate::files::model::missing_parents::MissingParents;

#[test]
fn a_path_with_no_parents_answers_the_home_itself() {
    let fixture = HomeFixture::new();

    let parent = open_mutable_parent(
        fixture.home(),
        &mutable("notes.txt"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap();

    assert_eq!(
        parent.metadata().unwrap().ino(),
        std::fs::metadata(fixture.home()).unwrap().ino()
    );
}

#[test]
fn every_level_above_the_name_is_descended() {
    let fixture = HomeFixture::new();
    fixture.directory("a/b");

    let parent = open_mutable_parent(
        fixture.home(),
        &mutable("a/b/file"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap();

    assert_eq!(
        parent.metadata().unwrap().ino(),
        std::fs::metadata(fixture.path("a/b")).unwrap().ino()
    );
}

#[test]
fn a_missing_level_is_refused_when_required() {
    let fixture = HomeFixture::new();

    let refused = open_mutable_parent(
        fixture.home(),
        &mutable("a/b/file"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
    assert!(!fixture.exists("a"));
}

#[test]
fn a_missing_level_is_created_when_asked() {
    let fixture = HomeFixture::new();

    open_mutable_parent(
        fixture.home(),
        &mutable("a/b/file"),
        fixture.uid(),
        MissingParents::Create,
    )
    .unwrap();

    assert!(fixture.path("a/b").is_dir());
}

#[test]
fn a_symlink_level_is_refused_and_not_followed() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.link("a", elsewhere.path());

    let refused = open_mutable_parent(
        fixture.home(),
        &mutable("a/file"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
}
