//! Taking an entry, and everything below it, out of a tree.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::remove_tree;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;
use crate::files::tree::directory_access::DirectoryAccess;
use crate::files::tree::tree_limits::TreeLimits;

fn removed(fixture: &HomeFixture, name: &str) -> Result<u64, FilesOpError> {
    removed_with(fixture, name, DirectoryAccess::AsFound)
}

fn removed_with(
    fixture: &HomeFixture,
    name: &str,
    access: DirectoryAccess,
) -> Result<u64, FilesOpError> {
    let mut count = 0;
    remove_tree(
        &fixture.open(""),
        OsStr::new(name),
        fixture.uid(),
        fixture.device(""),
        0,
        access,
        &mut count,
    )
    .map(|()| count)
}

#[test]
fn a_tree_is_removed_and_every_entry_is_counted() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");
    fixture.file("t/sub/b", "y");

    assert_eq!(removed(&fixture, "t"), Ok(4));
    assert!(!fixture.exists("t"));
}

#[test]
fn a_single_file_is_removed() {
    let fixture = HomeFixture::new();
    fixture.file("f", "x");

    assert_eq!(removed(&fixture, "f"), Ok(1));
    assert!(!fixture.exists("f"));
}

#[test]
fn a_symlink_inside_a_deleted_tree_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.directory("t");
    fixture.link("t/to-dir", &fixture.path("outside"));
    fixture.link("t/to-file", &fixture.path("outside/keep"));

    removed(&fixture, "t").unwrap();

    assert!(!fixture.exists("t"));
    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn a_symlink_as_the_root_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.link("l", &fixture.path("outside"));

    removed(&fixture, "l").unwrap();

    assert!(!fixture.exists("l"));
    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn force_deleting_a_hardlinked_file_keeps_the_other_name() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "shared");
    std::fs::hard_link(fixture.path("t/a"), fixture.path("keep")).unwrap();

    removed(&fixture, "t").unwrap();

    assert_eq!(fixture.read("keep"), "shared");
}

#[test]
fn a_fifo_in_the_tree_is_unlinked_without_hanging() {
    let fixture = HomeFixture::new();
    fixture.fifo("t/pipe");

    assert_eq!(removed(&fixture, "t"), Ok(2));
}

#[test]
fn deleting_a_tree_does_not_descend_into_a_directory_on_another_filesystem() {
    let fixture = HomeFixture::new();
    fixture.file("t/d/inner", "x");
    let mut count = 0;

    let refused = remove_tree(
        &fixture.open(""),
        OsStr::new("t"),
        fixture.uid(),
        fixture.device("").wrapping_add(1),
        0,
        DirectoryAccess::AsFound,
        &mut count,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::CrossesDevice);
    assert_eq!(fixture.read("t/d/inner"), "x");
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused_not_half_deleted() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; TreeLimits::MAXIMUM_DEPTH + 1].join("/");
    fixture.file(&format!("t/{deep}/leaf"), "x");

    assert_eq!(removed(&fixture, "t"), Err(FilesOpError::TooDeep));
    assert!(fixture.exists("t"));
}

#[test]
fn a_run_that_was_stopped_is_finished_by_running_it_again() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");
    fixture.file("t/sub/b", "y");
    // What a killed first run leaves: part of the tree gone.
    std::fs::remove_file(fixture.path("t/a")).unwrap();

    assert!(removed(&fixture, "t").is_ok());
    assert!(!fixture.exists("t"));
}

#[test]
fn an_entry_that_is_not_there_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(removed(&fixture, "nothing"), Err(FilesOpError::NotFound));
}

#[test]
fn a_directory_of_another_owner_is_refused_and_left_alone() {
    let fixture = HomeFixture::new();
    fixture.file("t/inner", "x");
    let mut count = 0;

    let refused = remove_tree(
        &fixture.open(""),
        OsStr::new("t"),
        fixture.uid().wrapping_add(1),
        fixture.device(""),
        0,
        DirectoryAccess::AsFound,
        &mut count,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::NotOwned);
    assert_eq!(fixture.read("t/inner"), "x");
}

#[test]
fn a_read_only_directory_is_refused_as_found_and_the_tree_stays() {
    let fixture = HomeFixture::new();
    fixture.file("t/ro/inner", "x");
    fixture.set_mode("t/ro", 0o555);

    let result = removed(&fixture, "t");
    let kept = fixture.exists("t/ro/inner");
    fixture.set_mode("t/ro", 0o700);

    // As root a mode forbids nothing, so the control only holds for an account.
    if fixture.uid() != 0 {
        assert_eq!(result, Err(FilesOpError::RemoveFailed));
        assert!(kept);
    }
}

#[test]
fn a_read_only_directory_is_widened_and_removed_when_reclaimed() {
    let fixture = HomeFixture::new();
    fixture.file("t/ro/deeper/inner", "x");
    fixture.set_mode("t/ro/deeper", 0o500);
    fixture.set_mode("t/ro", 0o555);

    assert_eq!(
        removed_with(&fixture, "t", DirectoryAccess::Reclaimed),
        Ok(4)
    );
    assert!(!fixture.exists("t"));
}
