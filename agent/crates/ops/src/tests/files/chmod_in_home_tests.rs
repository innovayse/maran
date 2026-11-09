//! Changing permission bits inside a customer's home.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fs::file_mode::FileMode;

use super::chmod_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};
use crate::files::model::permission_report::PermissionReport;

fn changed(
    fixture: &HomeFixture,
    path: &str,
    mode: u32,
    directory_mode: u32,
    recursive: bool,
) -> Result<PermissionReport, FilesOpError> {
    chmod_in_home(
        fixture.home(),
        &mutable(path),
        FileMode::parse(mode).unwrap(),
        FileMode::parse(directory_mode).unwrap(),
        recursive,
        fixture.uid(),
    )
}

#[test]
fn one_file_gets_the_mode() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    let report = changed(&fixture, "a", 0o600, 0o755, false).unwrap();

    assert_eq!(fixture.mode("a"), 0o600);
    assert_eq!((report.changed, report.skipped), (1, 0));
}

#[test]
fn setting_the_same_mode_twice_is_success_both_times() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    changed(&fixture, "a", 0o600, 0o755, false).unwrap();

    assert!(changed(&fixture, "a", 0o600, 0o755, false).is_ok());
}

#[test]
fn a_directory_without_recursion_gets_the_mode_and_its_children_are_untouched() {
    let fixture = HomeFixture::new();
    fixture.file("d/f", "x");
    fixture.set_mode("d/f", 0o600);

    changed(&fixture, "d", 0o750, 0o700, false).unwrap();

    assert_eq!(fixture.mode("d"), 0o750);
    assert_eq!(fixture.mode("d/f"), 0o600);
}

#[test]
fn recursion_gives_files_the_mode_and_directories_the_directory_mode() {
    let fixture = HomeFixture::new();
    fixture.file("d/f", "x");
    fixture.file("d/s/g", "y");

    let report = changed(&fixture, "d", 0o644, 0o755, true).unwrap();

    assert_eq!(fixture.mode("d/f"), 0o644);
    assert_eq!(fixture.mode("d/s/g"), 0o644);
    assert_eq!(fixture.mode("d/s"), 0o755);
    assert_eq!(fixture.mode("d"), 0o755);
    assert_eq!((report.changed, report.skipped), (4, 0));
}

#[test]
fn recursion_on_a_plain_file_is_the_same_as_no_recursion() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    let report = changed(&fixture, "a", 0o640, 0o755, true).unwrap();

    assert_eq!(fixture.mode("a"), 0o640);
    assert_eq!(report.changed, 1);
}

#[test]
fn a_symlink_as_the_path_is_refused_and_its_target_is_unchanged() {
    let fixture = HomeFixture::new();
    fixture.file("target", "x");
    fixture.set_mode("target", 0o600);
    fixture.link("l", &fixture.path("target"));

    assert_eq!(
        changed(&fixture, "l", 0o777, 0o777, false),
        Err(FilesOpError::UnsupportedEntry)
    );
    assert_eq!(fixture.mode("target"), 0o600);
}

#[test]
fn a_symlink_as_the_root_of_a_recursive_change_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file("outside/f", "x");
    fixture.set_mode("outside/f", 0o600);
    fixture.link("l", &fixture.path("outside"));

    assert_eq!(
        changed(&fixture, "l", 0o777, 0o777, true),
        Err(FilesOpError::UnsupportedEntry)
    );
    assert_eq!(fixture.mode("outside/f"), 0o600);
}

#[test]
fn a_missing_path_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(
        changed(&fixture, "nothing", 0o600, 0o755, false),
        Err(FilesOpError::NotFound)
    );
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused_before_the_first_bit_changes() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; 130].join("/");
    fixture.file(&format!("t/{deep}/leaf"), "x");
    fixture.file("t/early", "x");
    fixture.set_mode("t/early", 0o600);

    assert_eq!(
        changed(&fixture, "t", 0o644, 0o755, true),
        Err(FilesOpError::TooDeep)
    );
    assert_eq!(fixture.mode("t/early"), 0o600);
}

#[test]
fn a_recursive_change_skips_a_link_inside_and_says_so() {
    let fixture = HomeFixture::new();
    fixture.file("outside/f", "x");
    fixture.set_mode("outside/f", 0o600);
    fixture.file("t/a", "x");
    fixture.link("t/l", &fixture.path("outside/f"));

    let report = changed(&fixture, "t", 0o644, 0o755, true).unwrap();

    assert_eq!(fixture.mode("outside/f"), 0o600);
    assert_eq!((report.changed, report.skipped), (2, 1));
}
