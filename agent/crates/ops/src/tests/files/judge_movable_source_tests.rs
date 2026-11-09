//! What may be moved or trashed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::judge_movable_source;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

fn judged(fixture: &HomeFixture, name: &str, uid: u32) -> Result<(), FilesOpError> {
    judge_movable_source(&fixture.open(""), OsStr::new(name), uid)
}

#[test]
fn a_file_the_account_owns_may_be_moved() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    assert_eq!(judged(&fixture, "a", fixture.uid()), Ok(()));
}

#[test]
fn a_directory_the_account_owns_may_be_moved() {
    let fixture = HomeFixture::new();
    fixture.directory("a");

    assert_eq!(judged(&fixture, "a", fixture.uid()), Ok(()));
}

#[test]
fn a_fifo_may_be_moved_and_is_never_opened() {
    let fixture = HomeFixture::new();
    fixture.fifo("a");

    assert_eq!(judged(&fixture, "a", fixture.uid()), Ok(()));
}

#[test]
fn an_absent_entry_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(
        judged(&fixture, "a", fixture.uid()),
        Err(FilesOpError::NotFound)
    );
}

#[test]
fn a_symlink_is_refused_whether_or_not_it_resolves() {
    let fixture = HomeFixture::new();
    fixture.file("target", "x");
    fixture.link("good", &fixture.path("target"));
    fixture.link("dangling", &fixture.path("nowhere"));

    for name in ["good", "dangling"] {
        assert_eq!(
            judged(&fixture, name, fixture.uid()),
            Err(FilesOpError::UnsupportedEntry)
        );
    }
}

#[test]
fn an_entry_of_another_owner_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    assert_eq!(
        judged(&fixture, "a", fixture.uid().wrapping_add(1)),
        Err(FilesOpError::NotOwned)
    );
}
