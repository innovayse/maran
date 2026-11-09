//! Moving one entry back out of the account's trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use super::restore_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};
use crate::files::trash_in_home::trash_in_home;

const ID: &str = "1700000000-00000000000000aa";

fn id() -> TrashEntryId {
    TrashEntryId::parse(ID).unwrap()
}

/// The path of `file` inside the trash entry, or of the entry itself when `file` is empty.
fn entry(file: &str) -> String {
    let entry = format!(
        "{}/{}/{ID}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    );
    if file.is_empty() {
        entry
    } else {
        format!("{entry}/{file}")
    }
}

/// Trashes `path` under the fixed identifier, so a restore has something to restore.
fn trash(fixture: &HomeFixture, path: &str) {
    trash_in_home(fixture.home(), &mutable(path), &id(), fixture.uid()).unwrap();
}

fn restored(fixture: &HomeFixture, to: &str) -> Result<(), FilesOpError> {
    restore_in_home(fixture.home(), &id(), &mutable(to), fixture.uid())
}

#[test]
fn a_trashed_file_comes_back_with_its_content_and_the_entry_is_gone() {
    let fixture = HomeFixture::new();
    fixture.file("docs/a.txt", "hello");
    trash(&fixture, "docs/a.txt");

    restored(&fixture, "docs/a.txt").unwrap();

    assert_eq!(fixture.read("docs/a.txt"), "hello");
    assert!(!fixture.exists(&entry("")));
}

#[test]
fn a_trashed_directory_comes_back_whole_to_a_destination_whose_parents_are_created() {
    let fixture = HomeFixture::new();
    fixture.file("d/inner/f", "x");
    trash(&fixture, "d");

    restored(&fixture, "elsewhere/deep/d").unwrap();

    assert_eq!(fixture.read("elsewhere/deep/d/inner/f"), "x");
}

#[test]
fn restoring_onto_an_existing_name_reports_already_exists_and_keeps_the_entry() {
    let fixture = HomeFixture::new();
    fixture.file("a", "trashed");
    trash(&fixture, "a");
    fixture.file("a", "newer");

    assert_eq!(restored(&fixture, "a"), Err(FilesOpError::AlreadyExists));
    assert_eq!(fixture.read("a"), "newer");
    assert_eq!(
        fixture.read(&entry(AgentPaths::TRASH_PAYLOAD_NAME)),
        "trashed"
    );
}

#[test]
fn an_unknown_identifier_is_not_found() {
    let fixture = HomeFixture::new();
    fixture.directory(&format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    ));

    assert_eq!(restored(&fixture, "a"), Err(FilesOpError::NotFound));
}

#[test]
fn a_trash_never_used_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(restored(&fixture, "a"), Err(FilesOpError::NotFound));
}

#[test]
fn an_entry_without_a_payload_is_an_invalid_entry() {
    let fixture = HomeFixture::new();
    fixture.directory(&entry(""));

    assert_eq!(
        restored(&fixture, "a"),
        Err(FilesOpError::InvalidTrashEntry)
    );
}

#[test]
fn an_entry_that_is_a_plain_file_is_an_invalid_entry() {
    let fixture = HomeFixture::new();
    fixture.file(&entry(""), "not a directory");

    assert_eq!(
        restored(&fixture, "a"),
        Err(FilesOpError::InvalidTrashEntry)
    );
}

#[test]
fn a_symlink_level_on_the_destination_path_is_refused_and_the_entry_stays() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.file("a", "x");
    trash(&fixture, "a");
    fixture.link("dir", elsewhere.path());

    assert_eq!(
        restored(&fixture, "dir/a"),
        Err(FilesOpError::DirectoryUnusable)
    );
    assert!(!elsewhere.path().join("a").exists());
    assert!(fixture.exists(&entry(AgentPaths::TRASH_PAYLOAD_NAME)));
}
