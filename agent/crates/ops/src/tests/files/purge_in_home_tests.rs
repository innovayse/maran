//! Removing the trash entries older than a retention.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use super::purge_in_home;
use crate::files::home_fixture::HomeFixture;

const DAY: u64 = 86_400;
const NOW: u64 = 1_700_000_000;

fn entries() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    )
}

fn staging() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME
    )
}

/// An identifier made `age` seconds before `NOW`.
fn id_aged(age: u64) -> String {
    format!("{}-{:016x}", NOW - age, age)
}

fn trashed(fixture: &HomeFixture, age: u64) {
    fixture.file(&format!("{}/{}/payload/f", entries(), id_aged(age)), "x");
}

fn purged(fixture: &HomeFixture, days: u32) -> u64 {
    purge_in_home(fixture.home(), days, NOW, fixture.uid()).unwrap()
}

#[test]
fn purge_keeps_entries_younger_than_the_retention_and_removes_older_ones() {
    let fixture = HomeFixture::new();
    trashed(&fixture, 10 * DAY);
    trashed(&fixture, 29 * DAY);
    trashed(&fixture, 31 * DAY);
    trashed(&fixture, 40 * DAY);

    assert_eq!(purged(&fixture, 30), 2);

    let mut kept = vec![id_aged(10 * DAY), id_aged(29 * DAY)];
    kept.sort();
    assert_eq!(fixture.names(&entries()), kept);
}

#[test]
fn an_entry_exactly_the_retention_old_is_kept() {
    let fixture = HomeFixture::new();
    trashed(&fixture, 30 * DAY);

    assert_eq!(purged(&fixture, 30), 0);
    assert_eq!(fixture.names(&entries()).len(), 1);
}

#[test]
fn an_entry_one_second_past_the_retention_is_removed() {
    let fixture = HomeFixture::new();
    trashed(&fixture, 30 * DAY + 1);

    assert_eq!(purged(&fixture, 30), 1);
    assert!(fixture.names(&entries()).is_empty());
}

#[test]
fn a_name_that_is_not_an_identifier_is_left_alone_unless_it_is_an_empty_directory() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/loose-file", entries()), "x");
    fixture.file(&format!("{}/stray-with-content/f", entries()), "x");
    fixture.directory(&format!("{}/stray-empty", entries()));

    assert_eq!(purged(&fixture, 1), 1);
    assert_eq!(
        fixture.names(&entries()),
        vec!["loose-file".to_owned(), "stray-with-content".to_owned()]
    );
}

#[test]
fn a_link_inside_an_expired_entry_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.directory(&format!("{}/{}", entries(), id_aged(60 * DAY)));
    fixture.link(
        &format!("{}/{}/payload", entries(), id_aged(60 * DAY)),
        &fixture.path("outside"),
    );

    purged(&fixture, 30);

    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn staging_older_than_a_day_is_swept_and_younger_staging_is_kept() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/{}/copy/f", staging(), id_aged(2 * DAY)), "x");
    fixture.file(&format!("{}/{}/copy/f", staging(), id_aged(DAY / 2)), "x");

    purged(&fixture, 30);

    assert_eq!(fixture.names(&staging()), vec![id_aged(DAY / 2)]);
}

#[test]
fn staging_sweeps_do_not_count_as_trash_entries_removed() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/{}/copy/f", staging(), id_aged(2 * DAY)), "x");

    assert_eq!(purged(&fixture, 30), 0);
}

#[test]
fn a_trash_never_used_is_success_with_zero() {
    let fixture = HomeFixture::new();

    assert_eq!(purged(&fixture, 30), 0);
}

#[test]
fn an_identifier_dated_in_the_future_is_kept() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!("{}/{}-{:016x}/payload", entries(), NOW + DAY, 1),
        "x",
    );

    assert_eq!(purged(&fixture, 1), 0);
}

#[test]
fn an_expired_entry_holding_a_read_only_directory_is_purged_and_a_young_one_is_kept() {
    let fixture = HomeFixture::new();
    let expired = format!("{}/{}/payload/ro", entries(), id_aged(60 * DAY));
    let young = format!("{}/{}/payload/ro", entries(), id_aged(DAY));
    fixture.file(&format!("{expired}/inner"), "x");
    fixture.file(&format!("{young}/inner"), "x");
    fixture.set_mode(&expired, 0o555);
    fixture.set_mode(&young, 0o555);

    assert_eq!(purged(&fixture, 30), 1);

    assert_eq!(fixture.names(&entries()), vec![id_aged(DAY)]);
    assert!(
        fixture.exists(&format!("{young}/inner")),
        "the control: an unexpired entry is untouched"
    );
    assert_eq!(
        fixture.mode(&young),
        0o555,
        "the control: an unexpired directory keeps its mode"
    );
    fixture.set_mode(&young, 0o700);
}

#[test]
fn an_abandoned_copy_holding_a_read_only_directory_is_swept() {
    let fixture = HomeFixture::new();
    let copy = format!("{}/{}/copy/ro", staging(), id_aged(2 * DAY));
    fixture.file(&format!("{copy}/inner"), "x");
    fixture.set_mode(&copy, 0o555);

    purged(&fixture, 30);

    assert_eq!(fixture.names(&staging()), Vec::<String>::new());
}
