//! Where a trash entry came from, read as the account.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use super::trashed_original_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;

const ID: &str = "1700000000-00000000000000aa";

fn entries() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    )
}

fn id() -> TrashEntryId {
    TrashEntryId::parse(ID).unwrap()
}

#[test]
fn the_recorded_path_is_returned_as_found() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!("{}/{ID}/{}", entries(), AgentPaths::TRASH_ORIGINAL_NAME),
        "docs/a.txt",
    );

    let text = trashed_original_in_home(fixture.home(), &id(), fixture.uid()).unwrap();

    assert_eq!(text, "docs/a.txt");
}

#[test]
fn a_trash_never_used_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(
        trashed_original_in_home(fixture.home(), &id(), fixture.uid()).unwrap_err(),
        FilesOpError::NotFound
    );
}

#[test]
fn an_unknown_entry_is_not_found() {
    let fixture = HomeFixture::new();
    fixture.directory(&entries());

    assert_eq!(
        trashed_original_in_home(fixture.home(), &id(), fixture.uid()).unwrap_err(),
        FilesOpError::NotFound
    );
}

#[test]
fn an_entry_that_is_a_plain_file_is_an_invalid_entry() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/{ID}", entries()), "x");

    assert_eq!(
        trashed_original_in_home(fixture.home(), &id(), fixture.uid()).unwrap_err(),
        FilesOpError::InvalidTrashEntry
    );
}
