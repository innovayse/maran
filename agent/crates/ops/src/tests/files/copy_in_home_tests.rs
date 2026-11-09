//! Copying a file or a tree inside a customer's home, all or nothing.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::fs::transfer_bounds::TransferBounds;
use maran_agent_core::validation::fs::trash_entry_id::TrashEntryId;

use super::copy_in_home;
use crate::files::FilesOpError;
use crate::files::home_fixture::{HomeFixture, mutable};
use crate::files::model::copy_report::CopyReport;
use crate::files::model::limit_kind::LimitKind;

const STAGE: &str = "1700000000-00000000000000bb";

fn staging() -> String {
    format!(
        "{}/{}",
        AgentPaths::TRASH_DIRECTORY_NAME,
        AgentPaths::TRASH_STAGING_DIRECTORY_NAME
    )
}

fn copied(
    fixture: &HomeFixture,
    from: &str,
    to: &str,
    entries: u64,
    bytes: u64,
) -> Result<CopyReport, FilesOpError> {
    copy_in_home(
        fixture.home(),
        &mutable(from),
        &mutable(to),
        &TransferBounds::parse(entries, bytes).unwrap(),
        &TrashEntryId::parse(STAGE).unwrap(),
        fixture.uid(),
    )
}

#[test]
fn a_tree_is_copied_and_the_source_is_left_in_place() {
    let fixture = HomeFixture::new();
    fixture.file("src/a.txt", "alpha");
    fixture.file("src/sub/b.txt", "beta");

    let report = copied(&fixture, "src", "dst", 100, 1000).unwrap();

    assert_eq!(fixture.read("dst/a.txt"), "alpha");
    assert_eq!(fixture.read("dst/sub/b.txt"), "beta");
    assert_eq!(fixture.read("src/a.txt"), "alpha");
    assert_eq!((report.entries, report.bytes), (4, 9));
}

#[test]
fn a_single_file_is_copied_into_missing_parents() {
    let fixture = HomeFixture::new();
    fixture.file("a.txt", "solo");

    copied(&fixture, "a.txt", "new/dir/b.txt", 10, 100).unwrap();

    assert_eq!(fixture.read("new/dir/b.txt"), "solo");
}

#[test]
fn the_staging_area_is_empty_after_a_successful_copy() {
    let fixture = HomeFixture::new();
    fixture.file("a", "x");

    copied(&fixture, "a", "b", 10, 100).unwrap();

    assert_eq!(fixture.names(&staging()), Vec::<String>::new());
}

#[test]
fn copying_onto_an_existing_name_reports_already_exists_and_changes_nothing() {
    let fixture = HomeFixture::new();
    fixture.file("a", "first");
    fixture.file("b", "second");

    assert_eq!(
        copied(&fixture, "a", "b", 10, 100),
        Err(FilesOpError::AlreadyExists)
    );
    assert_eq!(fixture.read("b"), "second");
    assert!(!fixture.exists(AgentPaths::TRASH_DIRECTORY_NAME));
}

#[test]
fn a_missing_source_is_not_found() {
    let fixture = HomeFixture::new();

    assert_eq!(
        copied(&fixture, "a", "b", 10, 100),
        Err(FilesOpError::NotFound)
    );
}

#[test]
fn a_destination_inside_the_source_is_refused() {
    let fixture = HomeFixture::new();
    fixture.directory("d");

    assert_eq!(
        copied(&fixture, "d", "d/copy", 10, 100),
        Err(FilesOpError::IntoItself)
    );
    assert!(fixture.names("d").is_empty());
}

#[test]
fn a_link_anywhere_in_the_tree_refuses_the_whole_copy_before_anything_is_written() {
    let fixture = HomeFixture::new();
    fixture.file("outside/secret", "s");
    fixture.file("src/a", "x");
    fixture.link("src/l", &fixture.path("outside"));

    assert_eq!(
        copied(&fixture, "src", "dst", 100, 1000),
        Err(FilesOpError::UnsupportedEntry)
    );
    assert!(!fixture.exists("dst"));
    assert!(!fixture.exists(AgentPaths::TRASH_DIRECTORY_NAME));
}

#[test]
fn a_symlink_as_the_source_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file("target", "x");
    fixture.link("l", &fixture.path("target"));

    assert_eq!(
        copied(&fixture, "l", "dst", 10, 100),
        Err(FilesOpError::UnsupportedEntry)
    );
}

#[test]
fn a_fifo_in_the_tree_refuses_the_copy_without_hanging() {
    let fixture = HomeFixture::new();
    fixture.fifo("src/pipe");

    assert_eq!(
        copied(&fixture, "src", "dst", 10, 100),
        Err(FilesOpError::UnsupportedEntry)
    );
}

#[test]
fn a_copy_over_the_stated_entry_cap_reports_the_entry_limit_and_leaves_nothing() {
    let fixture = HomeFixture::new();
    for index in 0..5 {
        fixture.file(&format!("src/f{index}"), "x");
    }

    assert_eq!(
        copied(&fixture, "src", "dst", 4, 1000),
        Err(FilesOpError::LimitReached(LimitKind::Entries))
    );
    assert!(!fixture.exists("dst"));
}

#[test]
fn a_copy_over_the_stated_byte_cap_reports_the_byte_limit_and_leaves_nothing() {
    let fixture = HomeFixture::new();
    fixture.file("src/a", "123456");

    assert_eq!(
        copied(&fixture, "src", "dst", 100, 5),
        Err(FilesOpError::LimitReached(LimitKind::Bytes))
    );
    assert!(!fixture.exists("dst"));
}

#[test]
fn a_copy_that_fails_midway_leaves_nothing_at_the_destination_and_no_staging() {
    let fixture = HomeFixture::new();
    if fixture.uid() == 0 {
        eprintln!(
            "skipped: root can open a mode 000 file, so the copy cannot be made to fail midway"
        );
        return;
    }
    fixture.file("src/a", "x");
    fixture.file("src/b", "y");
    // Unreadable to its owner: the scan stats it fine and the copy cannot open it.
    fixture.set_mode("src/b", 0o000);

    let outcome = copied(&fixture, "src", "dst", 100, 1000);

    fixture.set_mode("src/b", 0o600);
    assert!(outcome.is_err());
    assert!(!fixture.exists("dst"));
    assert_eq!(fixture.names(&staging()), Vec::<String>::new());
}

#[test]
fn a_failed_copy_that_staged_a_read_only_directory_still_leaves_no_staging() {
    let fixture = HomeFixture::new();
    if fixture.uid() == 0 {
        eprintln!(
            "skipped: root renames into a mode 555 directory, so the copy cannot be made to fail"
        );
        return;
    }
    fixture.file("src/ro/deeper/inner", "x");
    fixture.set_mode("src/ro/deeper", 0o500);
    fixture.set_mode("src/ro", 0o555);
    // The copy completes in staging, then the final rename into this parent is refused.
    fixture.directory("locked");
    fixture.set_mode("locked", 0o555);

    let outcome = copied(&fixture, "src", "locked/dst", 100, 1000);

    fixture.set_mode("locked", 0o700);
    fixture.set_mode("src/ro", 0o700);
    fixture.set_mode("src/ro/deeper", 0o700);
    assert!(outcome.is_err());
    assert!(!fixture.exists("locked/dst"));
    assert_eq!(fixture.names(&staging()), Vec::<String>::new());
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused_before_anything_is_written() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; 130].join("/");
    fixture.directory(&format!("src/{deep}"));

    assert_eq!(
        copied(&fixture, "src", "dst", 1000, 1000),
        Err(FilesOpError::TooDeep)
    );
    assert!(!fixture.exists("dst"));
}

#[test]
fn a_symlink_level_on_the_destination_path_is_refused() {
    let fixture = HomeFixture::new();
    let elsewhere = tempfile::tempdir().unwrap();
    fixture.file("a", "x");
    fixture.link("dir", elsewhere.path());

    assert_eq!(
        copied(&fixture, "a", "dir/a", 10, 100),
        Err(FilesOpError::DirectoryUnusable)
    );
    assert!(!elsewhere.path().join("a").exists());
}
