//! Tests for the write itself: what lands on disk, and what is refused.
//!
//! Like the walk's tests, none of these needs root — the uid is an argument, and
//! a test owns its temporary directory exactly as a customer owns their home.
//! Each attack a hosting account can mount against a file the agent is about to
//! write has a test here: a symlink at the destination name, a pre-created file
//! at that name, a directory at that name, and a temporary name already taken.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::{MetadataExt, PermissionsExt, symlink};
use std::path::Path;

use maran_agent_core::utils::current_uid::current_uid;
use maran_agent_core::validation::fs::file_mode::FileMode;
use maran_agent_core::validation::fs::relative_path::RelativePath;

use super::write_in_home;
use crate::files::FilesOpError;
use crate::files::model::missing_parents::MissingParents;
use crate::files::open_parent_directory::open_parent_directory;

/// The challenge path every test here writes to.
const CHALLENGE: &str = "sites/example.com/.well-known/acme-challenge/token123";

/// The temporary name the write renames from.
const TEMPORARY: &str = ".maran-write-test";

/// Creates the challenge directory so a write has somewhere to land.
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

/// Writes `contents` at the challenge path with `mode`.
fn write(home: &Path, contents: &[u8], mode: u32) -> Result<(), FilesOpError> {
    write_in_home(
        home,
        &RelativePath::parse(CHALLENGE).unwrap(),
        TEMPORARY,
        contents,
        FileMode::parse(mode).expect("the tests only use plain permission modes"),
        current_uid().unwrap(),
    )
}

/// Where the challenge file ends up.
fn challenge(home: &Path) -> std::path::PathBuf {
    home.join(CHALLENGE)
}

/// Where the temporary file would be if one were left behind.
fn temporary(home: &Path) -> std::path::PathBuf {
    home.join("sites/example.com/.well-known/acme-challenge")
        .join(TEMPORARY)
}
#[test]
fn the_content_lands_byte_for_byte_at_the_path_the_caller_named() {
    let home = prepared();

    write(home.path(), b"token123.key-authorization", 0o644).unwrap();

    assert_eq!(
        std::fs::read(challenge(home.path())).unwrap(),
        b"token123.key-authorization"
    );
}
#[test]
fn the_file_carries_exactly_the_mode_the_caller_asked_for_whatever_the_umask_is() {
    let home = prepared();

    write(home.path(), b"token", 0o644).unwrap();

    let mode = std::fs::metadata(challenge(home.path()))
        .unwrap()
        .permissions()
        .mode();
    assert_eq!(
        mode & 0o777,
        0o644,
        "a challenge the web server cannot read is an issuance that fails silently"
    );
}
#[test]
fn no_temporary_file_survives_a_successful_write() {
    let home = prepared();

    write(home.path(), b"token", 0o644).unwrap();

    assert!(!temporary(home.path()).exists());
}
#[test]
fn writing_twice_replaces_the_content_and_leaves_one_file() {
    let home = prepared();

    write(home.path(), b"first", 0o644).unwrap();
    write(home.path(), b"second", 0o644).unwrap();

    assert_eq!(std::fs::read(challenge(home.path())).unwrap(), b"second");
    assert!(!temporary(home.path()).exists());
}
#[test]
fn a_symlink_planted_at_the_destination_is_replaced_and_never_written_through() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("victim");
    std::fs::write(&target, b"original").unwrap();
    symlink(&target, challenge(home.path())).unwrap();

    write(home.path(), b"token", 0o644).unwrap();

    assert_eq!(
        std::fs::read(&target).unwrap(),
        b"original",
        "the write must replace the link, not follow it"
    );
    assert_eq!(std::fs::read(challenge(home.path())).unwrap(), b"token");
    assert!(
        !challenge(home.path())
            .symlink_metadata()
            .unwrap()
            .is_symlink()
    );
}
#[test]
fn a_dangling_symlink_at_the_destination_creates_nothing_where_it_pointed() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("was-never-there");
    symlink(&target, challenge(home.path())).unwrap();

    write(home.path(), b"token", 0o644).unwrap();

    assert!(
        !target.exists(),
        "following a dangling link would create the attacker's file for them"
    );
    assert_eq!(std::fs::read(challenge(home.path())).unwrap(), b"token");
}
#[test]
fn a_directory_at_the_destination_is_refused_rather_than_emptied() {
    let home = prepared();
    std::fs::create_dir(challenge(home.path())).unwrap();

    let refused = write(home.path(), b"token", 0o644).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
    assert!(challenge(home.path()).is_dir());
}
#[test]
fn a_failed_write_leaves_no_temporary_file_behind_in_the_customers_directory() {
    let home = prepared();
    std::fs::create_dir(challenge(home.path())).unwrap();

    write(home.path(), b"token", 0o644).unwrap_err();

    assert!(
        !temporary(home.path()).exists(),
        "a rename that failed must take its temporary file with it"
    );
}
#[test]
fn a_temporary_name_a_customer_occupied_first_is_refused_rather_than_written_through() {
    let home = prepared();
    let elsewhere = tempfile::tempdir().unwrap();
    let target = elsewhere.path().join("victim");
    std::fs::write(&target, b"original").unwrap();
    symlink(&target, temporary(home.path())).unwrap();

    let refused = write(home.path(), b"token", 0o644).unwrap_err();

    assert_eq!(refused, FilesOpError::WriteFailed);
    assert_eq!(
        std::fs::read(&target).unwrap(),
        b"original",
        "O_EXCL is what keeps the bytes out of a descriptor the customer chose"
    );
}
#[test]
fn a_missing_challenge_directory_is_refused_rather_than_created_by_the_write() {
    let home = tempfile::tempdir().unwrap();

    let refused = write(home.path(), b"token", 0o644).unwrap_err();

    assert_eq!(refused, FilesOpError::DirectoryUnusable);
}
#[test]
fn the_written_file_is_owned_by_the_account_the_write_ran_as() {
    let home = prepared();

    write(home.path(), b"token", 0o644).unwrap();

    assert_eq!(
        std::fs::metadata(challenge(home.path())).unwrap().uid(),
        current_uid().unwrap(),
        "a root-owned file in a customer's document root is one they cannot remove"
    );
}
