//! Making one directory in a customer's home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::file_mode::FileMode;

use super::create_directory_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};

fn created(fixture: &HomeFixture, path: &str, mode: u32) -> Result<(), FilesOpError> {
    create_directory_in_home(
        fixture.home(),
        &mutable(path),
        FileMode::parse(mode).unwrap(),
        fixture.uid(),
    )
}

#[test]
fn a_directory_is_created_with_exactly_the_requested_mode() {
    let fixture = HomeFixture::new();

    created(&fixture, "docs", 0o750).unwrap();

    assert!(fixture.path("docs").is_dir());
    assert_eq!(fixture.mode("docs"), 0o750);
}

#[test]
fn missing_parents_are_created_with_the_agents_directory_mode_and_only_the_last_gets_the_requested_one()
 {
    let fixture = HomeFixture::new();

    created(&fixture, "a/b/c", 0o700).unwrap();

    assert_eq!(fixture.mode("a"), 0o755);
    assert_eq!(fixture.mode("a/b"), 0o755);
    assert_eq!(fixture.mode("a/b/c"), 0o700);
}

#[test]
fn parents_that_exist_are_left_exactly_as_they_are() {
    let fixture = HomeFixture::new();
    fixture.directory("a");
    fixture.set_mode("a", 0o711);

    created(&fixture, "a/b", 0o755).unwrap();

    assert_eq!(fixture.mode("a"), 0o711);
}

#[test]
fn an_existing_directory_is_already_exists_and_keeps_its_mode() {
    let fixture = HomeFixture::new();
    fixture.directory("docs");
    fixture.set_mode("docs", 0o700);

    assert_eq!(
        created(&fixture, "docs", 0o755),
        Err(FilesOpError::AlreadyExists)
    );
    assert_eq!(fixture.mode("docs"), 0o700);
}

#[test]
fn a_file_at_the_name_is_already_exists_and_is_not_replaced() {
    let fixture = HomeFixture::new();
    fixture.file("docs", "i am a file");

    assert_eq!(
        created(&fixture, "docs", 0o755),
        Err(FilesOpError::AlreadyExists)
    );
    assert_eq!(fixture.read("docs"), "i am a file");
}

#[test]
fn a_symlink_at_the_name_is_already_exists_and_its_target_is_untouched() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.link("docs", elsewhere.path());

    assert_eq!(
        created(&fixture, "docs", 0o700),
        Err(FilesOpError::AlreadyExists)
    );
    assert!(
        std::fs::read_dir(elsewhere.path())
            .unwrap()
            .next()
            .is_none()
    );
}

#[test]
fn a_symlink_level_on_the_way_is_refused_and_nothing_is_created_through_it() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.link("a", elsewhere.path());

    assert_eq!(
        created(&fixture, "a/b", 0o755),
        Err(FilesOpError::DirectoryUnusable)
    );
    assert!(!elsewhere.path().join("b").exists());
}
