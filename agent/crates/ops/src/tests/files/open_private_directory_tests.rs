//! The private directories the trash is made of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;
use std::os::unix::fs::MetadataExt;

use super::open_private_directory;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;
use crate::files::model::missing_parents::MissingParents;

#[test]
fn a_directory_made_here_is_private_whatever_the_umask_was() {
    let fixture = HomeFixture::new();

    let opened = open_private_directory(
        &fixture.open(""),
        OsStr::new(".private"),
        fixture.uid(),
        MissingParents::Create,
    )
    .unwrap();

    assert!(opened.is_some());
    assert_eq!(fixture.mode(".private"), 0o700);
}

#[test]
fn an_existing_directory_keeps_its_mode() {
    let fixture = HomeFixture::new();
    fixture.directory(".private");
    fixture.set_mode(".private", 0o750);

    open_private_directory(
        &fixture.open(""),
        OsStr::new(".private"),
        fixture.uid(),
        MissingParents::Create,
    )
    .unwrap();

    assert_eq!(fixture.mode(".private"), 0o750);
}

#[test]
fn an_absent_directory_is_none_when_required_and_is_not_made() {
    let fixture = HomeFixture::new();

    let opened = open_private_directory(
        &fixture.open(""),
        OsStr::new(".private"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap();

    assert!(opened.is_none());
    assert!(!fixture.exists(".private"));
}

#[test]
fn a_symlink_at_the_name_is_refused_and_not_reported_as_absent() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.link(".private", elsewhere.path());

    for missing in [MissingParents::Require, MissingParents::Create] {
        let refused = open_private_directory(
            &fixture.open(""),
            OsStr::new(".private"),
            fixture.uid(),
            missing,
        )
        .unwrap_err();

        assert_eq!(refused, FilesOpError::DirectoryUnusable);
    }
}

#[test]
fn a_plain_file_at_the_name_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file(".private", "x");

    let refused = open_private_directory(
        &fixture.open(""),
        OsStr::new(".private"),
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
}

#[test]
fn a_directory_of_another_owner_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory(".private");
    let owner = std::fs::metadata(fixture.path(".private")).unwrap().uid();

    let refused = open_private_directory(
        &fixture.open(""),
        OsStr::new(".private"),
        owner.wrapping_add(1),
        MissingParents::Require,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
}
