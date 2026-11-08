//! Copying a tree into a directory that is being staged.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::CopyTree;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;
use crate::files::model::limit_kind::LimitKind;
use crate::files::tree::tree_limits::TreeLimits;

fn copied(fixture: &HomeFixture, limits: TreeLimits) -> (Result<(), FilesOpError>, CopyTree) {
    fixture.directory("stage");
    let mut copy = CopyTree::new(fixture.uid(), fixture.device(""), limits);
    let outcome = copy.copy(
        &fixture.open(""),
        OsStr::new("src"),
        &fixture.open("stage"),
        OsStr::new("out"),
        0,
    );
    (outcome, copy)
}

fn roomy() -> TreeLimits {
    TreeLimits::stated(1000, 1_000_000)
}

#[test]
fn a_tree_is_copied_with_its_content_and_counted() {
    let fixture = HomeFixture::new();
    fixture.file("src/a.txt", "alpha");
    fixture.file("src/sub/b.txt", "beta");

    let (outcome, copy) = copied(&fixture, roomy());

    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.read("stage/out/a.txt"), "alpha");
    assert_eq!(fixture.read("stage/out/sub/b.txt"), "beta");
    assert_eq!((copy.tally().entries, copy.tally().bytes), (4, 9));
}

#[test]
fn a_single_file_is_copied() {
    let fixture = HomeFixture::new();
    fixture.file("src", "solo");

    let (outcome, _) = copied(&fixture, roomy());

    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.read("stage/out"), "solo");
}

#[test]
fn permission_bits_are_kept_on_files_and_on_directories() {
    let fixture = HomeFixture::new();
    fixture.file("src/run.sh", "#!/bin/sh");
    fixture.set_mode("src/run.sh", 0o755);
    fixture.set_mode("src", 0o750);

    copied(&fixture, roomy()).0.unwrap();

    assert_eq!(fixture.mode("stage/out/run.sh"), 0o755);
    assert_eq!(fixture.mode("stage/out"), 0o750);
}

#[test]
fn a_read_only_source_directory_is_copied_and_the_copy_keeps_its_bits() {
    let fixture = HomeFixture::new();
    fixture.file("src/inner", "x");
    fixture.set_mode("src", 0o500);

    let (outcome, _) = copied(&fixture, roomy());

    fixture.set_mode("src", 0o700);
    assert_eq!(outcome, Ok(()));
    assert_eq!(fixture.mode("stage/out"), 0o500);
    fixture.set_mode("stage/out", 0o700);
}

#[test]
fn a_symlink_anywhere_in_the_tree_refuses_the_copy() {
    let fixture = HomeFixture::new();
    fixture.file("outside/secret", "s");
    fixture.directory("src");
    fixture.link("src/l", &fixture.path("outside"));

    let (outcome, _) = copied(&fixture, roomy());

    assert_eq!(outcome, Err(FilesOpError::UnsupportedEntry));
    assert!(!fixture.exists("stage/out/l"));
}

#[test]
fn a_fifo_in_the_tree_refuses_the_copy_without_hanging() {
    let fixture = HomeFixture::new();
    fixture.fifo("src/pipe");

    let (outcome, _) = copied(&fixture, roomy());

    assert_eq!(outcome, Err(FilesOpError::UnsupportedEntry));
}

#[test]
fn a_copy_over_the_stated_entry_cap_reports_the_entry_limit() {
    let fixture = HomeFixture::new();
    for index in 0..5 {
        fixture.file(&format!("src/f{index}"), "x");
    }

    let (outcome, _) = copied(&fixture, TreeLimits::stated(4, 1000));

    assert_eq!(outcome, Err(FilesOpError::LimitReached(LimitKind::Entries)));
}

#[test]
fn a_copy_over_the_stated_byte_cap_reports_the_byte_limit_by_counting_what_it_copies() {
    let fixture = HomeFixture::new();
    fixture.file("src/a", "123456");

    let (outcome, _) = copied(&fixture, TreeLimits::stated(100, 5));

    assert_eq!(outcome, Err(FilesOpError::LimitReached(LimitKind::Bytes)));
}

#[test]
fn a_copy_exactly_at_the_byte_cap_succeeds() {
    let fixture = HomeFixture::new();
    fixture.file("src/a", "12345");

    let (outcome, _) = copied(&fixture, TreeLimits::stated(100, 5));

    assert_eq!(outcome, Ok(()));
}

#[test]
fn a_missing_source_is_not_found() {
    let fixture = HomeFixture::new();

    let (outcome, _) = copied(&fixture, roomy());

    assert_eq!(outcome, Err(FilesOpError::NotFound));
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; TreeLimits::MAXIMUM_DEPTH + 1].join("/");
    fixture.directory(&format!("src/{deep}"));

    let (outcome, _) = copied(&fixture, roomy());

    assert_eq!(outcome, Err(FilesOpError::TooDeep));
}

#[test]
fn a_directory_on_another_filesystem_is_refused() {
    let fixture = HomeFixture::new();
    fixture.file("src/inner", "x");
    fixture.directory("stage");
    let mut copy = CopyTree::new(fixture.uid(), fixture.device("").wrapping_add(1), roomy());

    let outcome = copy.copy(
        &fixture.open(""),
        OsStr::new("src"),
        &fixture.open("stage"),
        OsStr::new("out"),
        0,
    );

    assert_eq!(outcome, Err(FilesOpError::CrossesDevice));
}
