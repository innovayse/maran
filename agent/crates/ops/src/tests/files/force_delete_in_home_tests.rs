//! Removing a file, a link or a tree from a customer's home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::force_delete_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};

fn deleted(fixture: &HomeFixture, path: &str) -> Result<u64, FilesOpError> {
    force_delete_in_home(fixture.home(), &mutable(path), fixture.uid())
}

#[test]
fn a_file_is_removed_and_counted() {
    let fixture = HomeFixture::new();
    fixture.file("a/b.txt", "x");

    assert_eq!(deleted(&fixture, "a/b.txt"), Ok(1));
    assert!(!fixture.exists("a/b.txt"));
    assert!(fixture.path("a").is_dir());
}

#[test]
fn a_whole_tree_is_removed_and_every_entry_is_counted() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");
    fixture.file("t/s/b", "y");

    assert_eq!(deleted(&fixture, "t"), Ok(4));
    assert!(!fixture.exists("t"));
}

#[test]
fn a_hardlinked_file_is_deleted_by_name_and_the_other_name_keeps_the_data() {
    let fixture = HomeFixture::new();
    fixture.file("a", "shared");
    std::fs::hard_link(fixture.path("a"), fixture.path("b")).unwrap();

    deleted(&fixture, "a").unwrap();

    assert_eq!(fixture.read("b"), "shared");
}

#[test]
fn a_symlink_is_unlinked_and_its_target_survives() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "kept");
    fixture.link("l", &fixture.path("outside"));

    deleted(&fixture, "l").unwrap();

    assert!(!fixture.exists("l"));
    assert_eq!(fixture.read("outside/keep"), "kept");
}

#[test]
fn a_dangling_symlink_is_unlinked() {
    let fixture = HomeFixture::new();
    fixture.link("l", &fixture.path("nowhere"));

    assert_eq!(deleted(&fixture, "l"), Ok(1));
}

#[test]
fn a_fifo_is_removed_without_hanging() {
    let fixture = HomeFixture::new();
    fixture.fifo("pipe");

    assert_eq!(deleted(&fixture, "pipe"), Ok(1));
}

#[test]
fn a_path_that_is_gone_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(deleted(&fixture, "nothing"), Err(FilesOpError::NotFound));
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused_and_not_half_deleted() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; 130].join("/");
    fixture.file(&format!("t/{deep}/leaf"), "x");

    assert_eq!(deleted(&fixture, "t"), Err(FilesOpError::TooDeep));
    assert!(fixture.exists("t"));
}

#[test]
fn a_symlink_level_on_the_way_is_refused_and_nothing_outside_is_removed() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    std::fs::write(elsewhere.path().join("victim"), "v").unwrap();
    fixture.link("dir", elsewhere.path());

    assert_eq!(
        deleted(&fixture, "dir/victim"),
        Err(FilesOpError::DirectoryUnusable)
    );
    assert!(elsewhere.path().join("victim").exists());
}
