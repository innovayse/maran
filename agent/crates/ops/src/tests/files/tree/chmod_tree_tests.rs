//! Changing the permission bits of a tree.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use maran_agent_core::validation::fs::file_mode::FileMode;

use super::ChmodTree;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;
use crate::files::model::permission_report::PermissionReport;

fn walked(fixture: &HomeFixture, uid: u32) -> (Result<(), FilesOpError>, PermissionReport) {
    let mut walk = ChmodTree::new(
        FileMode::parse(0o640).unwrap(),
        FileMode::parse(0o750).unwrap(),
        uid,
        fixture.device(""),
    );
    let outcome = walk.apply(&fixture.open(""), OsStr::new("t"), 0);
    (outcome, walk.report())
}

#[test]
fn files_get_the_file_mode_and_directories_get_the_directory_mode() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");
    fixture.file("t/sub/b", "y");

    let (outcome, report) = walked(&fixture, fixture.uid());

    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.mode("t/a"), 0o640);
    assert_eq!(fixture.mode("t/sub/b"), 0o640);
    assert_eq!(fixture.mode("t/sub"), 0o750);
    assert_eq!(fixture.mode("t"), 0o750);
    assert_eq!((report.changed, report.skipped), (4, 0));
}

#[test]
fn recursive_chmod_leaves_the_mode_of_a_linked_file_outside_the_tree_unchanged() {
    let fixture = HomeFixture::new();
    fixture.file("outside/keep", "x");
    fixture.set_mode("outside/keep", 0o600);
    fixture.directory("t");
    fixture.link("t/l", &fixture.path("outside/keep"));

    let (_, report) = walked(&fixture, fixture.uid());

    assert_eq!(fixture.mode("outside/keep"), 0o600);
    assert_eq!(report.skipped, 1);
}

#[test]
fn a_link_to_a_directory_is_not_entered_and_its_target_is_unchanged() {
    let fixture = HomeFixture::new();
    fixture.file("outside/inner", "x");
    fixture.set_mode("outside/inner", 0o600);
    fixture.directory("t");
    fixture.link("t/l", &fixture.path("outside"));

    walked(&fixture, fixture.uid()).0.unwrap();

    assert_eq!(fixture.mode("outside/inner"), 0o600);
}

#[test]
fn an_entry_of_another_owner_is_skipped_and_counted() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");
    fixture.set_mode("t/a", 0o600);

    let (outcome, report) = walked(&fixture, fixture.uid().wrapping_add(1));

    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.mode("t/a"), 0o600);
    assert_eq!((report.changed, report.skipped), (0, 1));
}

#[test]
fn a_fifo_has_its_mode_changed_without_being_blocked_on() {
    let fixture = HomeFixture::new();
    fixture.fifo("t/pipe");

    let (outcome, _) = walked(&fixture, fixture.uid());

    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.mode("t/pipe"), 0o640);
}

#[test]
fn a_directory_on_another_filesystem_is_skipped_and_not_entered() {
    let fixture = HomeFixture::new();
    fixture.file("t/d/inner", "x");
    fixture.set_mode("t/d/inner", 0o600);
    let mut walk = ChmodTree::new(
        FileMode::parse(0o640).unwrap(),
        FileMode::parse(0o750).unwrap(),
        fixture.uid(),
        fixture.device("").wrapping_add(1),
    );

    walk.apply(&fixture.open("t"), OsStr::new("d"), 0).unwrap();

    assert_eq!(fixture.mode("t/d/inner"), 0o600);
    assert_eq!(walk.report().skipped, 1);
}

#[test]
fn a_run_that_was_stopped_is_finished_by_running_it_again() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "x");

    walked(&fixture, fixture.uid()).0.unwrap();
    let (outcome, report) = walked(&fixture, fixture.uid());

    assert_eq!(outcome, Ok(()));
    assert_eq!(report.changed, 2);
}

#[test]
fn an_entry_that_vanished_is_neither_changed_nor_skipped() {
    let fixture = HomeFixture::new();
    fixture.directory("empty");

    let (outcome, report) = walked(&fixture, fixture.uid());

    assert_eq!(outcome, Ok(()));
    assert_eq!(report, PermissionReport::default());
}
