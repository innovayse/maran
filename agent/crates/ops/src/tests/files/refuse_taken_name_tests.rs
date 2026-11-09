//! A destination name is free only when nothing at all is there.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::refuse_taken_name;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

fn checked(fixture: &HomeFixture, name: &str) -> Result<(), FilesOpError> {
    refuse_taken_name(&fixture.open(""), OsStr::new(name))
}

#[test]
fn a_free_name_is_accepted() {
    let fixture = HomeFixture::new();

    assert_eq!(checked(&fixture, "a"), Ok(()));
}

#[test]
fn a_file_or_a_directory_at_the_name_is_already_exists() {
    let fixture = HomeFixture::new();
    fixture.file("file", "x");
    fixture.directory("directory");

    for name in ["file", "directory"] {
        assert_eq!(checked(&fixture, name), Err(FilesOpError::AlreadyExists));
    }
}

#[test]
fn a_dangling_symlink_at_the_name_counts_as_taken() {
    let fixture = HomeFixture::new();
    fixture.link("a", &fixture.path("nowhere"));

    assert_eq!(checked(&fixture, "a"), Err(FilesOpError::UnsupportedEntry));
}
