//! What the account's trash reports about itself.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use super::list_trash_in_home;
use crate::files::home_fixture::HomeFixture;

fn entries() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    )
}

/// Builds one well-formed trash entry the way `trash_in_home` leaves it.
fn entry(fixture: &HomeFixture, id: &str, original: &str, payload: &str) {
    fixture.file(
        &format!("{}/{id}/{}", entries(), AgentPaths::TRASH_ORIGINAL_NAME),
        original,
    );
    fixture.file(
        &format!("{}/{id}/{}", entries(), AgentPaths::TRASH_PAYLOAD_NAME),
        payload,
    );
}

fn listed(fixture: &HomeFixture) -> Vec<crate::files::model::trashed_entry::TrashedEntry> {
    list_trash_in_home(fixture.home(), fixture.uid()).unwrap()
}

#[test]
fn a_trash_never_used_is_an_empty_listing() {
    let fixture = HomeFixture::new();

    assert!(listed(&fixture).is_empty());
    assert!(!fixture.exists(AgentPaths::TRASH_DIRECTORY_NAME));
}

#[test]
fn an_entry_is_reported_with_its_original_path_time_kind_and_size() {
    let fixture = HomeFixture::new();
    entry(
        &fixture,
        "1700000000-00000000000000aa",
        "docs/a.txt",
        "12345",
    );

    let entries = listed(&fixture);

    assert_eq!(entries.len(), 1);
    assert_eq!(entries[0].trash_id, "1700000000-00000000000000aa");
    assert_eq!(entries[0].original_path, "docs/a.txt");
    assert_eq!(entries[0].trashed_at_unix, 1_700_000_000);
    assert!(!entries[0].is_directory);
    assert_eq!(entries[0].size_bytes, 5);
}

#[test]
fn a_trashed_directory_is_reported_as_one_with_size_zero() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!(
            "{}/1700000000-00000000000000aa/{}/f",
            entries(),
            AgentPaths::TRASH_PAYLOAD_NAME
        ),
        "x",
    );

    let entries = listed(&fixture);

    assert!(entries[0].is_directory);
    assert_eq!(entries[0].size_bytes, 0);
}

#[test]
fn entries_are_listed_newest_first() {
    let fixture = HomeFixture::new();
    entry(&fixture, "1700000100-0000000000000001", "old", "x");
    entry(&fixture, "1700000300-0000000000000002", "newest", "x");
    entry(&fixture, "1700000200-0000000000000003", "middle", "x");

    let order: Vec<String> = listed(&fixture)
        .into_iter()
        .map(|entry| entry.original_path)
        .collect();

    assert_eq!(order, vec!["newest", "middle", "old"]);
}

#[test]
fn a_directory_that_is_not_a_trash_id_is_ignored() {
    let fixture = HomeFixture::new();
    entry(&fixture, "1700000000-00000000000000aa", "real", "x");
    entry(&fixture, "not-an-id", "forged", "x");
    fixture.file(&format!("{}/loose-file", entries()), "x");

    let found = listed(&fixture);

    assert_eq!(found.len(), 1);
    assert_eq!(found[0].original_path, "real");
}

#[test]
fn an_entry_without_a_payload_is_left_out() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!(
            "{}/1700000000-00000000000000aa/{}",
            entries(),
            AgentPaths::TRASH_ORIGINAL_NAME
        ),
        "half-made",
    );

    assert!(listed(&fixture).is_empty());
}

#[test]
fn an_entry_whose_record_is_missing_is_listed_with_an_empty_original_path() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!(
            "{}/1700000000-00000000000000aa/{}",
            entries(),
            AgentPaths::TRASH_PAYLOAD_NAME
        ),
        "x",
    );

    let found = listed(&fixture);

    assert_eq!(found.len(), 1);
    assert_eq!(found[0].original_path, "");
}

#[test]
fn a_record_with_a_control_character_is_shown_as_empty() {
    let fixture = HomeFixture::new();
    entry(&fixture, "1700000000-00000000000000aa", "bad\x1bname", "x");

    assert_eq!(listed(&fixture)[0].original_path, "");
}

#[test]
fn a_symlink_in_place_of_the_payload_is_listed_without_following_it() {
    let fixture = HomeFixture::new();
    fixture.file("secret", "s");
    fixture.directory(&format!("{}/1700000000-00000000000000aa", entries()));
    fixture.link(
        &format!(
            "{}/1700000000-00000000000000aa/{}",
            entries(),
            AgentPaths::TRASH_PAYLOAD_NAME
        ),
        &fixture.path("secret"),
    );

    let found = listed(&fixture);

    assert_eq!(found.len(), 1);
    assert_eq!(found[0].size_bytes, 0);
    assert!(!found[0].is_directory);
}
