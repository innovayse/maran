//! The staging tree is made by root, handed over, and filled by the account.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{create_dir, write};
use std::os::unix::fs::MetadataExt as _;
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use crate::backup::model::extract_identity::ExtractIdentity;
use crate::backup::recording_backup_host::RecordingBackupHost;

use super::*;

/// The account every fixture belongs to.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The ids a test can really hand a directory to: its own.
fn own_ids(root: &std::path::Path) -> (u32, u32) {
    let metadata = root.symlink_metadata().unwrap();
    (current_uid().unwrap(), metadata.gid())
}

/// The staging directory is created root-only and then handed to the account,
/// and the extraction runs as the account (R3).
#[test]
fn the_home_is_extracted_as_the_account_into_a_staging_tree_it_owns() {
    let host = RecordingBackupHost::new();
    let root = TempDir::new().unwrap();
    let staging = root.path().join("alice.one");
    let (uid, gid) = own_ids(root.path());

    extract_home_as_account(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        &staging,
        &account(),
        uid,
        gid,
    )
    .unwrap();

    let metadata = staging.symlink_metadata().unwrap();
    assert!(metadata.is_dir());
    assert_eq!(metadata.mode() & 0o7777, 0o700);
    assert_eq!(metadata.uid(), uid);
    assert_eq!(
        host.extractions(),
        vec![("home".to_owned(), ExtractIdentity::Account(account()))]
    );
}

/// Litter from a crashed earlier attempt at this same restore is removed rather
/// than extracted over, because a stale tree would be swapped into the home as
/// if this run had built it.
#[test]
fn a_staging_tree_left_by_a_crashed_attempt_is_removed_first() {
    let host = RecordingBackupHost::new();
    let root = TempDir::new().unwrap();
    let staging = root.path().join("alice.one");
    std::fs::create_dir_all(&staging).unwrap();
    write(staging.join("stale"), b"from an earlier run").unwrap();
    let (uid, gid) = own_ids(root.path());

    extract_home_as_account(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        &staging,
        &account(),
        uid,
        gid,
    )
    .unwrap();

    assert!(!staging.join("stale").exists());
}

/// A staging root left `0700` by an earlier run is REPAIRED, not left alone.
///
/// The defect this covers is not that the mode was wrong once: it is that
/// `DirBuilder` never touches a directory that already exists, so a host that
/// ran one restore under the old code would have kept a root-only staging root
/// forever and every later restore would have failed on it. A fix that only
/// created the right mode passes on a fresh host and leaves every used host
/// broken, which is why the fixture here starts from the wrong mode.
#[test]
fn a_staging_root_left_root_only_by_an_earlier_run_is_repaired() {
    let host = RecordingBackupHost::new();
    let root = TempDir::new().unwrap();
    let staging_root = root.path().join("staging-root");
    create_dir(&staging_root).unwrap();
    set_permissions(&staging_root, Permissions::from_mode(0o700)).unwrap();

    let staging = staging_root.join("alice.one");
    let (uid, gid) = own_ids(root.path());

    extract_home_as_account(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        &staging,
        &account(),
        uid,
        gid,
    )
    .unwrap();

    assert_eq!(
        staging_root.symlink_metadata().unwrap().mode() & 0o7777,
        0o711
    );
    assert_eq!(staging.symlink_metadata().unwrap().mode() & 0o7777, 0o700);
}

/// A staging root that is not there at all is created traversable — and the
/// staging directory inside it stays the account's alone.
///
/// The inverse control for the case above: the same assertion has to hold from
/// both starting states, and a repair that only ever ran on an existing
/// directory would pass that case and fail every fresh host.
#[test]
fn a_missing_staging_root_is_created_traversable_but_not_listable() {
    let host = RecordingBackupHost::new();
    let root = TempDir::new().unwrap();
    let staging_root = root.path().join("never-made");
    let staging = staging_root.join("alice.one");
    let (uid, gid) = own_ids(root.path());

    extract_home_as_account(
        &host,
        &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
        &staging,
        &account(),
        uid,
        gid,
    )
    .unwrap();

    let mode = staging_root.symlink_metadata().unwrap().mode() & 0o7777;
    assert_eq!(mode, 0o711);
    assert_eq!(mode & 0o044, 0, "the root must not be listable");
}

/// Something that is not a directory sitting where the staging root belongs is
/// refused rather than removed: only root can write there, so it is an
/// operator's doing or a defect, and replacing it would be this code destroying
/// a thing it does not understand.
#[test]
fn a_staging_root_that_is_not_a_directory_is_refused() {
    let host = RecordingBackupHost::new();
    let root = TempDir::new().unwrap();
    let staging_root = root.path().join("not-a-directory");
    write(&staging_root, b"an operator put this here").unwrap();

    let staging = staging_root.join("alice.one");
    let (uid, gid) = own_ids(root.path());

    assert_eq!(
        extract_home_as_account(
            &host,
            &PathBuf::from("/var/backups/maran/alice/one.tar.gz"),
            &staging,
            &account(),
            uid,
            gid,
        ),
        Err(BackupError::StagingUnusable)
    );
    assert!(staging_root.is_file(), "it must still be there");
}
