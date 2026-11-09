//! Moving one entry into the account's trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use super::trash_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};

const ID: &str = "1700000000-00000000000000aa";

fn entry(file: &str) -> String {
    format!(
        "{}/{}/{ID}/{file}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    )
}

fn trashed(fixture: &HomeFixture, path: &str) -> Result<(), FilesOpError> {
    trash_in_home(
        fixture.home(),
        &mutable(path),
        &TrashEntryId::parse(ID).unwrap(),
        fixture.uid(),
    )
}

#[test]
fn a_file_moves_into_the_trash_with_a_record_of_where_it_was() {
    let fixture = HomeFixture::new();
    fixture.file("sites/a/index.html", "hello");

    trashed(&fixture, "sites/a/index.html").unwrap();

    assert!(!fixture.exists("sites/a/index.html"));
    assert_eq!(
        fixture.read(&entry(AgentPaths::TRASH_PAYLOAD_NAME)),
        "hello"
    );
    assert_eq!(
        fixture.read(&entry(AgentPaths::TRASH_ORIGINAL_NAME)),
        "sites/a/index.html"
    );
}

#[test]
fn a_directory_moves_whole_and_keeps_counting_inside_the_home() {
    let fixture = HomeFixture::new();
    fixture.file("d/inner/f", "x");

    trashed(&fixture, "d").unwrap();

    assert_eq!(
        fixture.read(&format!(
            "{}/inner/f",
            entry(AgentPaths::TRASH_PAYLOAD_NAME)
        )),
        "x"
    );
}

#[test]
fn the_trash_and_its_entry_are_private_to_the_account() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    trashed(&fixture, "a").unwrap();

    assert_eq!(fixture.mode(AgentPaths::TRASH_DIRECTORY_NAME), 0o700);
    assert_eq!(fixture.mode(&entry("")), 0o700);
}

#[test]
fn a_missing_source_is_not_found_and_leaves_no_trash_entry_behind() {
    let fixture = HomeFixture::new();

    assert_eq!(trashed(&fixture, "nothing"), Err(FilesOpError::NotFound));
    assert!(!fixture.exists(&entry("")));
}

#[test]
fn a_symlink_is_refused_and_stays_where_it_is() {
    let fixture = HomeFixture::new();
    fixture.file("target", "x");
    fixture.link("l", &fixture.path("target"));

    assert_eq!(trashed(&fixture, "l"), Err(FilesOpError::UnsupportedEntry));
    assert!(fixture.exists("l"));
}

#[test]
fn an_identifier_already_in_use_is_refused_and_the_existing_entry_is_untouched() {
    let fixture = HomeFixture::new();
    fixture.file("a", "first");
    fixture.file("b", "second");
    trashed(&fixture, "a").unwrap();

    assert_eq!(trashed(&fixture, "b"), Err(FilesOpError::MutationFailed));
    assert_eq!(
        fixture.read(&entry(AgentPaths::TRASH_PAYLOAD_NAME)),
        "first"
    );
    assert_eq!(fixture.read("b"), "second");
}
