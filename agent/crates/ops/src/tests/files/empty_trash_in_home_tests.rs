//! Removing everything in the account's trash.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;

use super::empty_trash_in_home;
use crate::files::home_fixture::HomeFixture;

fn entries() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_ENTRIES_DIRECTORY_NAME
    )
}

#[test]
fn a_trash_never_used_is_success_with_zero() {
    let fixture = HomeFixture::new();

    assert_eq!(empty_trash_in_home(fixture.home(), fixture.uid()), Ok(0));
    assert!(!fixture.exists(AgentPaths::TRASH_DIRECTORY_NAME));
}

#[test]
fn every_entry_is_removed_whatever_shape_it_has_and_counted() {
    let fixture = HomeFixture::new();
    fixture.file(
        &format!("{}/1700000000-00000000000000aa/payload/f", entries()),
        "x",
    );
    fixture.file(&format!("{}/not-an-id/f", entries()), "x");
    fixture.file(&format!("{}/loose", entries()), "x");

    assert_eq!(empty_trash_in_home(fixture.home(), fixture.uid()), Ok(3));
    assert_eq!(fixture.names(&entries()), Vec::<String>::new());
}

#[test]
fn a_link_inside_an_entry_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.directory(&format!("{}/1700000000-00000000000000aa", entries()));
    fixture.link(
        &format!("{}/1700000000-00000000000000aa/payload", entries()),
        &fixture.path("outside"),
    );

    empty_trash_in_home(fixture.home(), fixture.uid()).unwrap();

    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn the_copy_staging_area_is_left_alone() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/e/f", entries()), "x");
    let staging = format!(
        "{}/{}/1700000000-00000000000000bb/copy",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME
    );
    fixture.directory(&staging);

    empty_trash_in_home(fixture.home(), fixture.uid()).unwrap();

    assert!(fixture.path(&staging).is_dir());
}

#[test]
fn emptying_twice_is_success_the_second_time() {
    let fixture = HomeFixture::new();
    fixture.file(&format!("{}/e/f", entries()), "x");
    empty_trash_in_home(fixture.home(), fixture.uid()).unwrap();

    assert_eq!(empty_trash_in_home(fixture.home(), fixture.uid()), Ok(0));
}

#[test]
fn an_entry_holding_a_read_only_directory_is_emptied_like_any_other() {
    let fixture = HomeFixture::new();
    let entry = format!("{}/1700000000-00000000000000cc/payload/ro", entries());
    fixture.file(&format!("{entry}/inner"), "x");
    fixture.file(
        &format!("{}/1700000000-00000000000000dd/payload/f", entries()),
        "x",
    );
    fixture.set_mode(&entry, 0o555);

    assert_eq!(empty_trash_in_home(fixture.home(), fixture.uid()), Ok(2));
    assert_eq!(fixture.names(&entries()), Vec::<String>::new());
}
