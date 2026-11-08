//! Tests for the real directory handle, against a temporary directory.
//!
//! The handle is built the way the host builds it, with the test's own uid in
//! place of root's; everything below that choice is the production code.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::symlink;
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;

use super::ProcessLogDirectory;
use crate::sites::SiteLogDirectory;
use crate::sites::SitesOpError;
use crate::sites::open_site_log_directory::open_site_log_directory;

fn directory_at(path: &Path) -> ProcessLogDirectory {
    ProcessLogDirectory {
        file: open_site_log_directory(path, current_uid().unwrap()).unwrap(),
        uid: current_uid().unwrap(),
    }
}

#[test]
fn the_listing_names_every_entry_of_the_pinned_directory() {
    let temporary = tempfile::tempdir().unwrap();
    std::fs::write(temporary.path().join("a.com.access.log"), "x").unwrap();
    std::fs::write(temporary.path().join("a.com.access.log.1"), "x").unwrap();
    std::fs::create_dir(temporary.path().join("sub")).unwrap();

    let mut names = directory_at(temporary.path()).list_names().unwrap();
    names.sort();

    assert_eq!(names, vec!["a.com.access.log", "a.com.access.log.1", "sub"]);
}

#[test]
fn the_listing_follows_the_pinned_inode_and_not_the_path() {
    let parent = tempfile::tempdir().unwrap();
    let pinned = parent.path().join("pinned");
    std::fs::create_dir(&pinned).unwrap();
    std::fs::write(pinned.join("inside"), "x").unwrap();
    let directory = directory_at(&pinned);

    // The path is swapped for another directory after the pin was taken.
    std::fs::rename(&pinned, parent.path().join("moved")).unwrap();
    std::fs::create_dir(&pinned).unwrap();
    std::fs::write(pinned.join("impostor"), "x").unwrap();

    assert_eq!(directory.list_names().unwrap(), vec!["inside"]);
}

#[test]
fn a_regular_file_is_removed_by_name() {
    let temporary = tempfile::tempdir().unwrap();
    std::fs::write(temporary.path().join("a.com.access.log.3.gz"), "x").unwrap();

    let removed = directory_at(temporary.path())
        .remove_file("a.com.access.log.3.gz")
        .unwrap();

    assert!(removed);
    assert!(!temporary.path().join("a.com.access.log.3.gz").exists());
}

#[test]
fn a_directory_with_a_matching_name_is_left_alone() {
    let temporary = tempfile::tempdir().unwrap();
    std::fs::create_dir(temporary.path().join("a.com.access.log.2")).unwrap();

    let removed = directory_at(temporary.path())
        .remove_file("a.com.access.log.2")
        .unwrap();

    assert!(!removed);
    assert!(temporary.path().join("a.com.access.log.2").is_dir());
}

#[test]
fn a_symlink_with_a_matching_name_is_left_alone_and_its_target_is_untouched() {
    let temporary = tempfile::tempdir().unwrap();
    let outside = tempfile::tempdir().unwrap();
    std::fs::write(outside.path().join("target"), "keep").unwrap();
    symlink(
        outside.path().join("target"),
        temporary.path().join("a.com.access.log.2"),
    )
    .unwrap();

    let removed = directory_at(temporary.path())
        .remove_file("a.com.access.log.2")
        .unwrap();

    assert!(!removed);
    assert!(
        temporary
            .path()
            .join("a.com.access.log.2")
            .symlink_metadata()
            .is_ok()
    );
    assert_eq!(
        std::fs::read(outside.path().join("target")).unwrap(),
        b"keep"
    );
}

#[test]
fn removing_a_name_that_is_gone_is_the_idempotent_outcome() {
    let temporary = tempfile::tempdir().unwrap();

    assert!(
        !directory_at(temporary.path())
            .remove_file("a.com.access.log.9")
            .unwrap()
    );
}

#[test]
fn a_name_that_is_not_one_entry_is_refused_and_removes_nothing() {
    let temporary = tempfile::tempdir().unwrap();
    std::fs::write(temporary.path().join("keep"), "x").unwrap();
    std::fs::create_dir(temporary.path().join("sub")).unwrap();
    std::fs::write(temporary.path().join("sub").join("deep"), "x").unwrap();

    let outcome = directory_at(temporary.path()).remove_file("sub/deep");

    assert!(matches!(outcome, Err(SitesOpError::LogUnreadable { .. })));
    assert!(temporary.path().join("sub").join("deep").exists());
}

#[test]
fn an_opened_file_reports_its_length_and_serves_positioned_reads() {
    let temporary = tempfile::tempdir().unwrap();
    std::fs::write(temporary.path().join("a.com.access.log"), "0123456789").unwrap();
    let file = directory_at(temporary.path())
        .open_file("a.com.access.log")
        .unwrap()
        .expect("the file exists");
    let mut buffer = [0_u8; 4];

    assert_eq!(file.length().unwrap(), 10);
    assert_eq!(file.read_at(3, &mut buffer).unwrap(), 4);
    assert_eq!(&buffer, b"3456");
    assert_eq!(file.read_at(10, &mut buffer).unwrap(), 0);
}

#[test]
fn a_missing_file_is_none_and_not_an_error() {
    let temporary = tempfile::tempdir().unwrap();

    assert!(
        directory_at(temporary.path())
            .open_file("a.com.access.log")
            .unwrap()
            .is_none()
    );
}
