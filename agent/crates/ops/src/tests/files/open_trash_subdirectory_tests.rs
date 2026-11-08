//! Reaching the entries and staging directories of the trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use super::open_trash_subdirectory;
use crate::files::home_fixture::HomeFixture;
use crate::files::model::missing_parents::MissingParents;

#[test]
fn creating_makes_both_levels_private() {
    let fixture = HomeFixture::new();

    let opened = open_trash_subdirectory(
        &fixture.open(""),
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        fixture.uid(),
        MissingParents::Create,
    )
    .unwrap();

    assert!(opened.is_some());
    let trash = AgentPaths::TRASH_DIRECTORY_NAME;
    assert_eq!(fixture.mode(trash), 0o700);
    assert_eq!(
        fixture.mode(&format!(
            "{trash}/{}",
            AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
        )),
        0o700
    );
}

#[test]
fn a_trash_never_used_is_none_when_required_and_is_not_made() {
    let fixture = HomeFixture::new();

    let opened = open_trash_subdirectory(
        &fixture.open(""),
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME,
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap();

    assert!(opened.is_none());
    assert!(!fixture.exists(AgentPaths::TRASH_DIRECTORY_NAME));
}

#[test]
fn a_trash_without_the_requested_subdirectory_is_none() {
    let fixture = HomeFixture::new();
    fixture.directory(AgentPaths::TRASH_DIRECTORY_NAME);

    let opened = open_trash_subdirectory(
        &fixture.open(""),
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME,
        fixture.uid(),
        MissingParents::Require,
    )
    .unwrap();

    assert!(opened.is_none());
}
