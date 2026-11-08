//! Tests for the list of what an extraction made.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;

use maran_agent_core::utils::current_uid::current_uid;

use super::ExtractionLedger;

fn roll_back(ledger: ExtractionLedger, home: &std::path::Path) {
    ledger.roll_back(&fs::File::open(home).unwrap(), current_uid().unwrap());
}

#[test]
fn files_and_directories_it_recorded_are_removed_newest_first() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("d/e")).unwrap();
    fs::write(home.path().join("d/e/f.txt"), b"x").unwrap();
    fs::write(home.path().join("d/g.txt"), b"x").unwrap();
    let mut ledger = ExtractionLedger::new();
    ledger.record_directory("d".to_owned());
    ledger.record_directory("d/e".to_owned());
    ledger.record_file("d/e/f.txt".to_owned());
    ledger.record_file("d/g.txt".to_owned());

    roll_back(ledger, home.path());

    assert!(!home.path().join("d").exists());
}

#[test]
fn only_what_it_recorded_is_removed() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("kept")).unwrap();
    fs::write(home.path().join("kept/theirs.txt"), b"x").unwrap();
    fs::write(home.path().join("kept/mine.txt"), b"x").unwrap();
    let mut ledger = ExtractionLedger::new();
    ledger.record_file("kept/mine.txt".to_owned());

    roll_back(ledger, home.path());

    assert!(home.path().join("kept/theirs.txt").exists());
    assert!(!home.path().join("kept/mine.txt").exists());
}

#[test]
fn a_directory_that_has_since_gained_something_is_left_alone() {
    let home = tempfile::tempdir().unwrap();
    fs::create_dir_all(home.path().join("made")).unwrap();
    fs::write(home.path().join("made/customer-added.txt"), b"x").unwrap();
    let mut ledger = ExtractionLedger::new();
    ledger.record_directory("made".to_owned());

    roll_back(ledger, home.path());

    assert!(home.path().join("made/customer-added.txt").exists());
}

#[test]
fn an_entry_that_is_already_gone_does_not_stop_the_rest() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("b.txt"), b"x").unwrap();
    let mut ledger = ExtractionLedger::new();
    ledger.record_file("b.txt".to_owned());
    ledger.record_file("gone/a.txt".to_owned());

    roll_back(ledger, home.path());

    assert!(!home.path().join("b.txt").exists());
}

#[test]
fn a_recorded_name_that_is_now_a_symlink_is_unlinked_and_its_target_survives() {
    let home = tempfile::tempdir().unwrap();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("precious.txt");
    fs::write(&target, b"keep").unwrap();
    std::os::unix::fs::symlink(&target, home.path().join("f.txt")).unwrap();
    let mut ledger = ExtractionLedger::new();
    ledger.record_file("f.txt".to_owned());

    roll_back(ledger, home.path());

    assert!(!home.path().join("f.txt").exists());
    assert_eq!(fs::read(&target).unwrap(), b"keep");
}

#[test]
fn an_empty_ledger_removes_nothing() {
    let home = tempfile::tempdir().unwrap();
    fs::write(home.path().join("a.txt"), b"x").unwrap();

    roll_back(ExtractionLedger::new(), home.path());

    assert!(home.path().join("a.txt").exists());
}
