//! Tests for the cursor that moves through a customer's tree by descriptor.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::os::unix::fs::{MetadataExt, symlink};

use maran_agent_core::utils::current_uid::current_uid;

use super::DirectoryCursor;
use crate::files::archive::archive_refusal::ArchiveRefusal;
use crate::files::archive::extraction_ledger::ExtractionLedger;
use crate::files::open_home::open_home;

fn cursor_in(home: &std::path::Path) -> DirectoryCursor {
    let uid = current_uid().unwrap();
    DirectoryCursor::new(open_home(home, uid).unwrap(), uid)
}

#[test]
fn entering_with_a_ledger_creates_the_missing_levels_and_records_them() {
    let home = tempfile::tempdir().unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    cursor.enter(&["a", "b", "c"], Some(&mut ledger)).unwrap();

    assert!(home.path().join("a/b/c").is_dir());
    ledger.roll_back(
        &fs::File::open(home.path()).unwrap(),
        current_uid().unwrap(),
    );
    assert!(
        !home.path().join("a").exists(),
        "everything it made was recorded"
    );
}

#[test]
fn a_level_it_creates_is_widened_to_0755_whatever_the_umask_made_of_it() {
    let home = tempfile::tempdir().unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    cursor.enter(&["made"], Some(&mut ledger)).unwrap();

    assert_eq!(
        fs::metadata(home.path().join("made")).unwrap().mode() & 0o7777,
        0o755
    );
}

#[test]
fn a_level_that_already_exists_is_entered_and_not_recorded() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir(home.path().join("old")).unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    cursor.enter(&["old", "new"], Some(&mut ledger)).unwrap();
    ledger.roll_back(
        &fs::File::open(home.path()).unwrap(),
        current_uid().unwrap(),
    );

    assert!(
        home.path().join("old").is_dir(),
        "the merged-into directory stays"
    );
    assert!(!home.path().join("old/new").exists());
}

#[test]
fn entering_without_a_ledger_never_creates_and_reports_a_missing_level() {
    let home = tempfile::tempdir().unwrap();
    let mut cursor = cursor_in(home.path());

    assert_eq!(
        cursor.enter(&["absent"], None).unwrap_err(),
        ArchiveRefusal::NotFound
    );
    assert!(!home.path().join("absent").exists());
}

#[test]
fn a_symlink_at_a_level_is_not_followed_when_reading() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    symlink(elsewhere.path(), home.path().join("link")).unwrap();
    let mut cursor = cursor_in(home.path());

    assert_eq!(
        cursor.enter(&["link"], None).unwrap_err(),
        ArchiveRefusal::DirectoryUnusable
    );
}

#[test]
fn a_symlink_at_a_level_is_a_taken_name_when_creating_and_nothing_is_written_through_it() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    symlink(elsewhere.path(), home.path().join("link")).unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    let refusal = cursor
        .enter(&["link", "inside"], Some(&mut ledger))
        .unwrap_err();

    assert_eq!(refusal, ArchiveRefusal::AlreadyExists);
    assert!(
        !elsewhere.path().join("inside").exists(),
        "the link's target was not touched"
    );
}

#[test]
fn a_regular_file_at_a_level_is_a_taken_name_when_creating() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("file"), b"x").unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    assert_eq!(
        cursor
            .enter(&["file", "inside"], Some(&mut ledger))
            .unwrap_err(),
        ArchiveRefusal::AlreadyExists
    );
}

#[test]
fn moving_between_siblings_reuses_the_shared_part_of_the_chain() {
    let home = tempfile::tempdir().unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();

    cursor.enter(&["a", "x"], Some(&mut ledger)).unwrap();
    cursor.enter(&["a", "y"], Some(&mut ledger)).unwrap();
    cursor.enter(&["a"], Some(&mut ledger)).unwrap();
    cursor.enter(&[], Some(&mut ledger)).unwrap();

    assert!(home.path().join("a/x").is_dir());
    assert!(home.path().join("a/y").is_dir());
}

#[test]
fn a_directory_swapped_for_a_link_between_two_entries_is_refused_not_followed() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    let mut cursor = cursor_in(home.path());
    let mut ledger = ExtractionLedger::new();
    cursor.enter(&["a"], Some(&mut ledger)).unwrap();
    cursor.enter(&[], Some(&mut ledger)).unwrap();

    fs::remove_dir(home.path().join("a")).unwrap();
    symlink(elsewhere.path(), home.path().join("a")).unwrap();

    assert!(cursor.enter(&["a", "inside"], Some(&mut ledger)).is_err());
    assert!(!elsewhere.path().join("inside").exists());
}
