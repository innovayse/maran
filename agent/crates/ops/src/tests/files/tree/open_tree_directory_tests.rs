//! Descending into one directory of a tree being walked.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::open_tree_directory;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

fn opened(fixture: &HomeFixture, name: &str, uid: u32, device: u64) -> Result<(), FilesOpError> {
    open_tree_directory(&fixture.open(""), OsStr::new(name), uid, device).map(|_| ())
}

#[test]
fn a_directory_the_account_owns_on_the_same_filesystem_is_opened() {
    let fixture = HomeFixture::new();
    fixture.directory("d");

    assert_eq!(
        opened(&fixture, "d", fixture.uid(), fixture.device("d")),
        Ok(())
    );
}

#[test]
fn a_symlink_to_a_directory_is_refused_and_not_followed() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.link("d", elsewhere.path());

    assert_eq!(
        opened(&fixture, "d", fixture.uid(), fixture.device("")),
        Err(FilesOpError::DirectoryUnusable)
    );
}

#[test]
fn a_plain_file_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file("d", "x");

    assert_eq!(
        opened(&fixture, "d", fixture.uid(), fixture.device("d")),
        Err(FilesOpError::DirectoryUnusable)
    );
}

#[test]
fn a_directory_of_another_owner_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory("d");

    assert_eq!(
        opened(
            &fixture,
            "d",
            fixture.uid().wrapping_add(1),
            fixture.device("d")
        ),
        Err(FilesOpError::NotOwned)
    );
}

#[test]
fn a_directory_on_another_filesystem_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory("d");

    assert_eq!(
        opened(
            &fixture,
            "d",
            fixture.uid(),
            fixture.device("d").wrapping_add(1)
        ),
        Err(FilesOpError::CrossesDevice)
    );
}
