//! The record of where a trashed entry came from is read as hostile text.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use super::read_trash_original;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

fn record() -> String {
    format!("entry/{}", AgentPaths::TRASH_ORIGINAL_NAME)
}

#[test]
fn the_recorded_text_is_returned_as_found() {
    let fixture = HomeFixture::new();
    fixture.file(&record(), "sites/a/index.html");

    let text = read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap();

    assert_eq!(text, "sites/a/index.html");
}

#[test]
fn a_missing_record_is_an_invalid_entry() {
    let fixture = HomeFixture::new();
    fixture.directory("entry");

    assert_eq!(
        read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_record_longer_than_a_path_can_be_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file(&record(), &"a".repeat(4097));

    assert_eq!(
        read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_record_that_is_a_symlink_is_refused_and_not_followed() {
    let fixture = HomeFixture::new();
    fixture.file("secret", "not-a-path");
    fixture.directory("entry");
    fixture.link(&record(), &fixture.path("secret"));

    assert_eq!(
        read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_record_that_is_a_fifo_is_refused_without_blocking() {
    let fixture = HomeFixture::new();
    fixture.directory("entry");
    fixture.fifo(&record());

    assert_eq!(
        read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}

#[test]
fn a_record_that_is_not_utf8_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory("entry");
    std::fs::write(fixture.path(&record()), [0xff, 0xfe]).unwrap();

    assert_eq!(
        read_trash_original(&fixture.open("entry"), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}
