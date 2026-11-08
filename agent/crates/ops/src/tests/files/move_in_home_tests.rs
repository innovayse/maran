//! Moving or renaming one entry inside a customer's home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::move_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};

fn moved(fixture: &HomeFixture, from: &str, to: &str) -> Result<(), FilesOpError> {
    move_in_home(fixture.home(), &mutable(from), &mutable(to), fixture.uid())
}

#[test]
fn a_file_is_renamed_in_place() {
    let fixture = HomeFixture::new();
    fixture.file("a.txt", "content");

    moved(&fixture, "a.txt", "b.txt").unwrap();

    assert!(!fixture.exists("a.txt"));
    assert_eq!(fixture.read("b.txt"), "content");
}

#[test]
fn a_directory_moves_with_everything_in_it_and_creates_the_missing_parents_of_the_destination() {
    let fixture = HomeFixture::new();
    fixture.file("src/inner/f", "x");

    moved(&fixture, "src", "new/place/dst").unwrap();

    assert!(!fixture.exists("src"));
    assert_eq!(fixture.read("new/place/dst/inner/f"), "x");
}

#[test]
fn moving_onto_an_existing_name_reports_already_exists_and_keeps_both() {
    let fixture = HomeFixture::new();
    fixture.file("a", "first");
    fixture.file("b", "second");

    assert_eq!(moved(&fixture, "a", "b"), Err(FilesOpError::AlreadyExists));
    assert_eq!(fixture.read("a"), "first");
    assert_eq!(fixture.read("b"), "second");
}

#[test]
fn moving_onto_an_existing_directory_does_not_move_into_it() {
    let fixture = HomeFixture::new();
    fixture.file("a", "first");
    fixture.directory("d");

    assert_eq!(moved(&fixture, "a", "d"), Err(FilesOpError::AlreadyExists));
    assert_eq!(fixture.names("d"), Vec::<String>::new());
}

#[test]
fn moving_a_path_onto_itself_reports_already_exists() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    assert_eq!(moved(&fixture, "a", "a"), Err(FilesOpError::AlreadyExists));
    assert_eq!(fixture.read("a"), "x");
}

#[test]
fn moving_a_directory_into_its_own_subtree_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory("d/sub");

    assert_eq!(
        moved(&fixture, "d", "d/sub/d2"),
        Err(FilesOpError::IntoItself)
    );
    assert!(fixture.path("d/sub").is_dir());
}

#[test]
fn a_sibling_whose_name_merely_starts_like_the_source_is_not_inside_it() {
    let fixture = HomeFixture::new();
    fixture.directory("d");

    moved(&fixture, "d", "d2").unwrap();

    assert!(fixture.path("d2").is_dir());
}

#[test]
fn a_missing_source_is_not_found_which_is_also_what_a_retry_sees() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");
    moved(&fixture, "a", "b").unwrap();

    assert_eq!(moved(&fixture, "a", "b"), Err(FilesOpError::NotFound));
}

#[test]
fn a_symlink_source_is_refused_and_its_target_stays() {
    let fixture = HomeFixture::new();
    fixture.file("target", "x");
    fixture.link("l", &fixture.path("target"));

    assert_eq!(
        moved(&fixture, "l", "m"),
        Err(FilesOpError::UnsupportedEntry)
    );
    assert!(fixture.exists("l"));
}

#[test]
fn a_symlink_at_the_destination_is_refused_and_not_followed() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.file("a", "x");
    fixture.link("dst", elsewhere.path());

    assert_eq!(
        moved(&fixture, "a", "dst"),
        Err(FilesOpError::UnsupportedEntry)
    );
    assert!(
        std::fs::read_dir(elsewhere.path())
            .unwrap()
            .next()
            .is_none()
    );
    assert_eq!(fixture.read("a"), "x");
}

#[test]
fn a_symlink_level_on_the_destination_path_is_refused() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.file("a", "x");
    fixture.link("dir", elsewhere.path());

    assert_eq!(
        moved(&fixture, "a", "dir/a"),
        Err(FilesOpError::DirectoryUnusable)
    );
    assert!(!elsewhere.path().join("a").exists());
}

#[test]
fn an_account_that_does_not_own_the_home_is_refused_before_anything_moves() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    let refused = move_in_home(
        fixture.home(),
        &mutable("a"),
        &mutable("b"),
        fixture.uid().wrapping_add(1),
    );

    // The home itself is not the other uid's, so the walk is what refuses.
    assert_eq!(refused, Err(FilesOpError::HomeUnusable));
    assert_eq!(fixture.read("a"), "x");
}
