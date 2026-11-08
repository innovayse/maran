//! Measuring a tree before anything is done to it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::ffi::OsStr;

use super::scan_tree;
use crate::files::FilesOpError;
use crate::files::home_fixture::HomeFixture;
use crate::files::model::limit_kind::LimitKind;
use crate::files::tree::tree_limits::TreeLimits;
use crate::files::tree::tree_tally::TreeTally;

fn scanned(
    fixture: &HomeFixture,
    name: &str,
    limits: TreeLimits,
) -> Result<TreeTally, FilesOpError> {
    let mut tally = TreeTally::default();
    scan_tree(
        &fixture.open(""),
        OsStr::new(name),
        fixture.uid(),
        fixture.device(""),
        0,
        &limits,
        &mut tally,
    )
    .map(|()| tally)
}

fn roomy() -> TreeLimits {
    TreeLimits::stated(1000, 1_000_000)
}

#[test]
fn a_plain_tree_is_counted_entry_by_entry_and_byte_by_byte() {
    let fixture = HomeFixture::new();
    fixture.file("t/a.txt", "12345");
    fixture.file("t/sub/b.txt", "123");

    let tally = scanned(&fixture, "t", roomy()).unwrap();

    assert_eq!((tally.entries, tally.bytes), (4, 8));
    assert_eq!(
        (tally.unsupported, tally.crossings, tally.foreign),
        (0, 0, 0)
    );
}

#[test]
fn a_symlink_is_counted_as_unsupported_and_its_target_is_not_read() {
    let fixture = HomeFixture::new();
    fixture.file("outside/big", &"x".repeat(5000));
    fixture.directory("t");
    fixture.link("t/l", &fixture.path("outside"));

    let tally = scanned(&fixture, "t", roomy()).unwrap();

    assert_eq!((tally.unsupported, tally.bytes), (1, 0));
}

#[test]
fn a_fifo_is_counted_as_unsupported_without_being_opened() {
    let fixture = HomeFixture::new();
    fixture.fifo("t/pipe");

    let tally = scanned(&fixture, "t", roomy()).unwrap();

    assert_eq!(tally.unsupported, 1);
}

#[test]
fn a_directory_on_another_filesystem_is_counted_and_not_entered() {
    let fixture = HomeFixture::new();
    fixture.file("t/d/inner", "x");
    let mut tally = TreeTally::default();

    scan_tree(
        &fixture.open("t"),
        OsStr::new("d"),
        fixture.uid(),
        fixture.device("t").wrapping_add(1),
        0,
        &roomy(),
        &mut tally,
    )
    .unwrap();

    assert_eq!((tally.crossings, tally.entries), (1, 1));
}

#[test]
fn a_directory_of_another_owner_is_counted_and_not_entered() {
    let fixture = HomeFixture::new();
    fixture.file("t/d/inner", "x");
    let mut tally = TreeTally::default();

    scan_tree(
        &fixture.open("t"),
        OsStr::new("d"),
        fixture.uid().wrapping_add(1),
        fixture.device("t"),
        0,
        &roomy(),
        &mut tally,
    )
    .unwrap();

    assert_eq!((tally.foreign, tally.entries), (1, 1));
}

#[test]
fn more_entries_than_the_allowance_is_the_entry_limit() {
    let fixture = HomeFixture::new();
    for index in 0..5 {
        fixture.file(&format!("t/f{index}"), "x");
    }

    assert_eq!(
        scanned(&fixture, "t", TreeLimits::stated(5, 1000)).unwrap_err(),
        FilesOpError::LimitReached(LimitKind::Entries)
    );
}

#[test]
fn exactly_the_allowance_is_accepted() {
    let fixture = HomeFixture::new();
    for index in 0..4 {
        fixture.file(&format!("t/f{index}"), "x");
    }

    assert!(scanned(&fixture, "t", TreeLimits::stated(5, 1000)).is_ok());
}

#[test]
fn more_bytes_than_the_allowance_is_the_byte_limit() {
    let fixture = HomeFixture::new();
    fixture.file("t/a", "123456");

    assert_eq!(
        scanned(&fixture, "t", TreeLimits::stated(100, 5)).unwrap_err(),
        FilesOpError::LimitReached(LimitKind::Bytes)
    );
}

#[test]
fn a_tree_deeper_than_the_ceiling_is_refused() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; TreeLimits::MAXIMUM_DEPTH + 1].join("/");
    fixture.directory(&format!("t/{deep}"));

    assert_eq!(
        scanned(&fixture, "t", roomy()).unwrap_err(),
        FilesOpError::TooDeep
    );
}

#[test]
fn a_tree_exactly_at_the_ceiling_is_accepted() {
    let fixture = HomeFixture::new();
    let deep = vec!["d"; TreeLimits::MAXIMUM_DEPTH - 1].join("/");
    fixture.directory(&format!("t/{deep}"));

    assert!(scanned(&fixture, "t", roomy()).is_ok());
}

#[test]
fn an_entry_that_is_not_there_counts_for_nothing() {
    let fixture = HomeFixture::new();

    let tally = scanned(&fixture, "nothing", roomy()).unwrap();

    assert_eq!(tally, TreeTally::default());
}
