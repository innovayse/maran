//! The bounds on a customer-supplied archive's listing.
//!
//! The listing is the pre-scan's evidence and it decides on the WHOLE
//! listing, so every test of a bound asks the same question: is the archive
//! refused whole, with no member handed on? The bounds are arguments of
//! `bounded_members` so they can be met at three members instead of two
//! million. One test also drives the real `tar` and `gzip` the Debian adapter
//! names, for the end-to-end shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::process::Command;

use maran_distro::{DistroFamily, adapter_for};

use super::{
    LISTING_REFUSED, LISTING_STDOUT_CAP, MAXIMUM_MEMBERS, MAXIMUM_NAME_BYTES, bounded_members,
    list_archive_members,
};
use crate::backup::backup_error::BackupError;

/// The listing a refusal for shape reports.
fn refused() -> BackupError {
    BackupError::ArchiveFailed {
        status: LISTING_REFUSED,
    }
}

/// Builds a gzip-compressed archive of `members` (files with a line of text)
/// under `directory` and returns its path.
///
/// Made by the platform's own `tar`, as a test of the real listing needs an
/// archive no hand-written fixture could stand in for.
fn real_archive(directory: &Path, members: &[&str]) -> std::path::PathBuf {
    let source = directory.join("source");
    std::fs::create_dir(&source).unwrap();
    for member in members {
        std::fs::write(source.join(member), "text\n").unwrap();
    }

    let artifact = directory.join("artifact.tar.gz");
    let status = Command::new("/usr/bin/tar")
        .arg("--create")
        .arg("--gzip")
        .arg("--file")
        .arg(&artifact)
        .arg("--directory")
        .arg(&source)
        .args(members)
        .status()
        .unwrap();
    assert!(status.success(), "the fixture archive was not made");

    artifact
}

#[test]
fn a_listing_within_both_bounds_is_returned_whole_and_in_order() {
    let members = bounded_members("a\nb\nc\n", 3, 8).unwrap();

    assert_eq!(members, vec!["a", "b", "c"]);
}

#[test]
fn a_listing_past_the_member_count_bound_refuses_the_whole_archive() {
    let outcome = bounded_members("a\nb\nc\nd\n", 3, 8);

    assert_eq!(outcome, Err(refused()));
}

/// The hostile member sits AFTER the bound. A test that planted it before the
/// bound would pass whether or not truncation reached the scan.
#[test]
fn a_hostile_member_after_the_count_bound_cannot_reach_the_scan_as_a_complete_listing() {
    let outcome = bounded_members("ok\nok\nok\n../etc/passwd\n", 3, 32);

    assert_eq!(outcome, Err(refused()));
}

#[test]
fn a_member_name_past_the_length_bound_refuses_the_archive() {
    let outcome = bounded_members("short\nabcdefghi\n", 10, 8);

    assert_eq!(outcome, Err(refused()));
}

/// The inverse control for the length bound: a name of exactly the bound is
/// accepted, so the comparison is neither off by one nor refusing everything.
#[test]
fn a_member_name_of_exactly_the_bound_is_accepted() {
    let members = bounded_members("abcdefgh\n", 10, 8).unwrap();

    assert_eq!(members, vec!["abcdefgh"]);
}

/// The inverse control for the count bound: exactly the bound is accepted.
#[test]
fn a_listing_of_exactly_the_member_count_bound_is_accepted() {
    let members = bounded_members("a\nb\nc\n", 3, 8).unwrap();

    assert_eq!(members.len(), 3);
}

#[test]
fn blank_lines_are_not_members_and_do_not_count_toward_the_bound() {
    let members = bounded_members("a\n\n\nb\n", 2, 8).unwrap();

    assert_eq!(members, vec!["a", "b"]);
}

#[test]
fn an_empty_listing_has_no_members() {
    assert_eq!(bounded_members("", 3, 8), Ok(Vec::new()));
}

/// Names come back exactly as the archive stores them: a `..` is not tidied
/// away here, because tidying it would decide the question the scan asks.
#[test]
fn names_are_returned_exactly_as_listed() {
    let members = bounded_members("../x\n/abs\n./y/\n", 10, 32).unwrap();

    assert_eq!(members, vec!["../x", "/abs", "./y/"]);
}

/// The production bounds admit every name `CreateBackup` can write: a path is
/// at most `PATH_MAX` bytes and `tar` prints a non-ASCII byte as a four-
/// character escape, so the printed form is at most four times as long.
#[test]
fn the_restore_bounds_admit_every_name_create_backup_can_write() {
    const PATH_MAX: usize = 4096;

    let name_bound = std::hint::black_box(MAXIMUM_NAME_BYTES);
    assert!(name_bound >= PATH_MAX);
    assert!(name_bound >= 4 * PATH_MAX);
    // The listing is read whole and split into owned strings, so the byte cap
    // must be able to hold the most members the count bound admits at even a
    // modest average name.
    let member_bound = u64::try_from(MAXIMUM_MEMBERS).unwrap();
    assert!(std::hint::black_box(LISTING_STDOUT_CAP) >= member_bound * 64);
}

#[test]
fn a_real_archive_is_listed_through_the_adapters_tar_and_gzip() {
    let root = tempfile::tempdir().unwrap();
    let artifact = real_archive(root.path(), &["one.txt", "two.txt"]);
    let adapter = adapter_for(DistroFamily::Debian);

    let members =
        list_archive_members(&adapter.tar_binary(), &adapter.gzip_binary(), &artifact).unwrap();

    assert_eq!(members, vec!["one.txt", "two.txt"]);
}

/// A file that is not a gzip stream is an archive failure with `tar`'s own
/// exit status, never an empty listing: an empty list would pass the scan.
#[test]
fn an_artifact_that_is_not_a_gzip_stream_is_an_archive_failure_and_not_an_empty_listing() {
    let root = tempfile::tempdir().unwrap();
    let artifact = root.path().join("artifact.tar.gz");
    std::fs::write(&artifact, "this is not an archive").unwrap();
    let adapter = adapter_for(DistroFamily::Debian);

    let outcome = list_archive_members(&adapter.tar_binary(), &adapter.gzip_binary(), &artifact);

    assert!(
        matches!(outcome, Err(BackupError::ArchiveFailed { status }) if status != 0),
        "expected an archive failure, got {outcome:?}"
    );
}

#[test]
fn an_artifact_that_does_not_exist_is_an_archive_failure() {
    let root = tempfile::tempdir().unwrap();
    let adapter = adapter_for(DistroFamily::Debian);

    let outcome = list_archive_members(
        &adapter.tar_binary(),
        &adapter.gzip_binary(),
        &root.path().join("absent.tar.gz"),
    );

    assert!(matches!(outcome, Err(BackupError::ArchiveFailed { .. })));
}

/// The listing of an archive is read under the listing deadline (one hour): a
/// large archive takes minutes to list, a shorter class would refuse it as if
/// hostile, and the bulk class would let a hostile archive hold the backup lock
/// for twelve hours.
#[test]
fn the_listing_is_spawned_under_the_listing_class_and_no_other() {
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../backup/list_archive_members.rs"
    ));

    assert_eq!(classes, ["Listing"]);
}

/// An output past its cap refuses the archive and never yields a partial list:
/// the spawn failure leaves through `?` before the members are split, and the
/// mapping it goes through keeps an overflow as a bound (see `bound_or_tests`).
#[test]
fn a_listing_that_overflows_its_cap_is_refused_and_never_returned_in_part() {
    let source = include_str!("../../backup/list_archive_members.rs");
    let production = source.split("#[cfg(test)]").next().unwrap();
    let mapped = production
        .find("bound_or(")
        .expect("the spawn error is mapped");
    let split = production
        .find("bounded_members(&outcome.stdout")
        .expect("the listing is split");

    assert!(
        mapped < split,
        "the spawn error must leave before any member is read"
    );
    assert!(
        production.contains(")?;"),
        "the mapped error is returned, not handled"
    );
}
