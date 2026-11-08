//! One trash entry, removed whole.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::remove_trash_entry;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

#[test]
fn an_entry_and_everything_in_it_is_removed() {
    let fixture = HomeFixture::new();
    fixture.file("entries/e/payload/deep/file", "x");
    fixture.file("entries/e/original", "a");

    remove_trash_entry(&fixture.open("entries"), OsStr::new("e"), fixture.uid()).unwrap();

    assert!(!fixture.exists("entries/e"));
}

#[test]
fn a_link_inside_the_entry_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.directory("entries/e/payload");
    fixture.link("entries/e/payload/link", &fixture.path("outside"));

    remove_trash_entry(&fixture.open("entries"), OsStr::new("e"), fixture.uid()).unwrap();

    assert!(!fixture.exists("entries/e"));
    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn an_entry_that_is_not_there_is_not_found() {
    let fixture = HomeFixture::new();
    fixture.directory("entries");

    assert_eq!(
        remove_trash_entry(&fixture.open("entries"), OsStr::new("e"), fixture.uid()).unwrap_err(),
        FilesOpError::NotFound
    );
}

#[test]
fn an_entry_holding_a_read_only_directory_is_widened_and_removed() {
    let fixture = HomeFixture::new();
    fixture.file("entries/e/payload/ro/deeper/inner", "x");
    fixture.set_mode("entries/e/payload/ro/deeper", 0o500);
    fixture.set_mode("entries/e/payload/ro", 0o555);

    remove_trash_entry(&fixture.open("entries"), OsStr::new("e"), fixture.uid()).unwrap();

    assert!(!fixture.exists("entries/e"));
}
