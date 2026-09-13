//! Tests for the removal: what it takes away, and what it refuses to touch.
//!
//! The three conditions `remove_in_home` checks — regular file, owned by the
//! account, one link — are mutated independently in review, so each has a test
//! that fails when only that condition is removed. A single test covering all
//! three would stay green while two of them were deleted.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::symlink;
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::relative_path::RelativePath;

use super::remove_in_home;
use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_parent_directory::open_parent_directory;

/// The challenge path every test here removes.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// The directory the challenge sits in, relative to the home.
const CHALLENGE_DIRECTORY: &str = "sites/example.com/.well-known/acme-challenge";

/// Creates the challenge directory so a removal has somewhere to look.
fn prepared() -> tempfile::TempDir {
    let home = tempfile::tempdir().unwrap();
    open_parent_directory(
        home.path(),
        &RelativePath::parse(CHALLENGE).unwrap(),
        current_uid().unwrap(),
        MissingParents::Create,
    )
    .unwrap();

    home
}

/// Removes the challenge file.
fn remove(home: &Path) -> Result<(), FilesOpError> {
    remove_in_home(
        home,
        &RelativePath::parse(CHALLENGE).unwrap(),
        current_uid().unwrap(),
    )
}

/// Where the challenge file lives.
fn challenge(home: &Path) -> std::path::PathBuf {
    home.join(CHALLENGE)
}

#[test]
fn an_ordinary_challenge_file_is_removed() {
    let home = prepared();
    std::fs::write(challenge(home.path()), b"token").unwrap();

    remove(home.path()).unwrap();

    assert!(!challenge(home.path()).exists());
}

#[test]
fn removing_a_file_that_is_already_gone_is_the_idempotent_not_found() {
    let home = prepared();

    assert_eq!(remove(home.path()).unwrap_err(), FilesOpError::NotFound);
}

#[test]
fn a_symlink_at_the_challenge_name_is_refused_rather_than_reported_as_nothing_there() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("victim");
    std::fs::write(&target, b"original").unwrap();
    symlink(&target, challenge(home.path())).unwrap();

    let refused = remove(home.path()).unwrap_err();

    assert_eq!(
        refused,
        FilesOpError::NotARegularFile,
        "a link refused by O_NOFOLLOW is somebody trying something, not an absence"
    );
    assert!(target.exists(), "the link's target must survive");
    assert!(challenge(home.path()).symlink_metadata().is_ok());
}

#[test]
fn a_hardlink_to_another_file_is_refused_by_the_link_count() {
    let home = prepared();
    let other = home.path().join(CHALLENGE_DIRECTORY).join("something-else");
    std::fs::write(&other, b"content").unwrap();
    std::fs::hard_link(&other, challenge(home.path())).unwrap();

    let refused = remove(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::NotARegularFile);
    assert!(
        challenge(home.path()).exists(),
        "a name with a second link is not the file the panel asked to remove"
    );
}

#[test]
fn a_directory_at_the_challenge_name_is_refused_and_not_taken_away() {
    let home = prepared();
    std::fs::create_dir(challenge(home.path())).unwrap();

    let refused = remove(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::NotARegularFile);
    assert!(challenge(home.path()).is_dir());
}

#[test]
fn a_fifo_at_the_challenge_name_is_refused_and_does_not_block_the_process() {
    let home = prepared();
    let made = std::process::Command::new("mkfifo")
        .arg(challenge(home.path()))
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .unwrap();
    assert!(made.success(), "the test needs mkfifo to build its fixture");

    // If `O_NONBLOCK` were dropped from `ENTRY_FLAGS` this call would never
    // return: opening a FIFO with no writer blocks in the kernel. The test
    // would then hang rather than fail, which is why the flag is named in the
    // constant's own doc comment and why this test exists at all.
    let refused = remove(home.path()).unwrap_err();

    assert_eq!(refused, FilesOpError::NotARegularFile);
    assert!(challenge(home.path()).symlink_metadata().is_ok());
}

#[test]
fn a_removal_told_a_uid_that_owns_nothing_on_the_path_is_refused_at_the_home() {
    let home = prepared();
    std::fs::write(challenge(home.path()), b"token").unwrap();

    // The uid the removal is told to expect is not the uid that owns the file,
    // which is the same asymmetry another account's file produces — and the
    // only way to produce it without root. The home and every level are handed
    // that same uid, so the walk refuses first, which makes this test blind to
    // the file's own ownership check; the file check is covered in the polygon.
    let refused = remove_in_home(
        home.path(),
        &RelativePath::parse(CHALLENGE).unwrap(),
        current_uid().unwrap() + 1,
    )
    .unwrap_err();

    assert_eq!(refused, FilesOpError::HomeUnusable);
    assert!(challenge(home.path()).exists());
}

#[test]
fn a_missing_challenge_directory_is_an_unusable_directory_and_not_an_absent_file() {
    let home = tempfile::tempdir().unwrap();

    assert_eq!(
        remove(home.path()).unwrap_err(),
        FilesOpError::DirectoryUnusable
    );
}
