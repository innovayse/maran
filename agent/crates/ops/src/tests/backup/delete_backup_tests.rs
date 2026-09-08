//! Deleting one backup: idempotent, and honest about which of the two files
//! it actually removed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use super::*;

/// A directory a test can really write into.
fn directory() -> tempfile::TempDir {
    tempfile::TempDir::new().expect("a temporary directory")
}

/// A backup id built from a fixed uuid string.
fn id(uuid: &str) -> BackupId {
    BackupId::parse(uuid).expect("the fixture id is a valid uuid")
}

/// Deleting a backup that was never published is [`BackupError::NotFound`],
/// not an error a caller has to route around — the proto's own promise, and
/// what makes a repeated delete safe.
#[test]
fn deleting_an_absent_backup_reports_not_found() {
    let directory = directory();
    let absent = id("55555555-5555-5555-5555-555555555555");

    let outcome = delete_in(directory.path(), &absent);

    assert_eq!(outcome, Err(BackupError::NotFound));
}

/// The ordinary case: both files vanish.
#[test]
fn deleting_removes_both_the_artifact_and_its_sidecar() {
    let directory = directory();
    let backup_id = id("66666666-6666-6666-6666-666666666666");
    let artifact = directory
        .path()
        .join(format!("{}.tar.gz", backup_id.as_str()));
    let sidecar = directory
        .path()
        .join(format!("{}.meta.json", backup_id.as_str()));
    File::create(&artifact).expect("the artifact fixture is writable");
    File::create(&sidecar).expect("the sidecar fixture is writable");

    delete_in(directory.path(), &backup_id).expect("the delete succeeds");

    assert!(artifact.symlink_metadata().is_err(), "the artifact remains");
    assert!(sidecar.symlink_metadata().is_err(), "the sidecar remains");
}

/// **The one the plan is specific about.** The artifact is the thing that
/// occupies the disk; a missing LABEL beside it is not a reason to leave the
/// disk space behind. Deleting still succeeds and the artifact is gone.
#[test]
fn a_missing_sidecar_beside_a_present_artifact_still_removes_the_artifact() {
    let directory = directory();
    let backup_id = id("77777777-7777-7777-7777-777777777777");
    let artifact = directory
        .path()
        .join(format!("{}.tar.gz", backup_id.as_str()));
    File::create(&artifact).expect("the artifact fixture is writable");

    delete_in(directory.path(), &backup_id).expect("the delete succeeds");

    assert!(artifact.symlink_metadata().is_err(), "the artifact remains");
}

/// A second delete of the same id, once the first has run, converges rather
/// than erroring — the same idempotence [`deleting_an_absent_backup_reports_not_found`]
/// exercises from a cold start.
#[test]
fn deleting_twice_is_safe() {
    let directory = directory();
    let backup_id = id("88888888-8888-8888-8888-888888888888");
    File::create(
        directory
            .path()
            .join(format!("{}.tar.gz", backup_id.as_str())),
    )
    .expect("the artifact fixture is writable");

    delete_in(directory.path(), &backup_id).expect("the first delete succeeds");
    let second = delete_in(directory.path(), &backup_id);

    assert_eq!(second, Err(BackupError::NotFound));
}

/// **F3, and the whole of what the caller sees.** A delete arriving while
/// another operation holds the account's lock — a restore reading the very
/// artifact retention wants to prune — is refused with
/// [`BackupError::AlreadyRunning`], immediately.
///
/// The root handed in is the real default one, which on this machine is
/// neither present nor root-owned: reaching it at all would answer
/// `BackupRootUnsafe`/`BackupRootUnusable`, so the assertion below is
/// simultaneously "the lock is taken" and "it is taken FIRST, before anything
/// touches the filesystem". Removing the lock line makes this test report one
/// of the root errors instead.
#[test]
fn deleting_while_the_account_is_locked_reports_already_running() {
    let account = AccountName::parse("delockd").expect("the fixture name is valid");
    let held = take_account_lock(&account);
    assert!(held.is_some(), "the fixture must own the lock");

    let outcome = delete_backup(
        &LocalBackupRoot::default(),
        &account,
        &id("99999999-9999-9999-9999-999999999999"),
    );

    assert_eq!(outcome, Err(BackupError::AlreadyRunning));
}

/// The lock is released by the delete's own return, so the next operation on
/// that account is not locked out by a delete that has finished — the inverse
/// control for the test above, which on its own passes just as well if the
/// guard were never dropped.
#[test]
fn a_finished_delete_leaves_the_account_unlocked() {
    let account = AccountName::parse("delockr").expect("the fixture name is valid");

    let _ = delete_backup(
        &LocalBackupRoot::default(),
        &account,
        &id("aaaaaaaa-9999-9999-9999-999999999999"),
    );

    assert!(
        take_account_lock(&account).is_some(),
        "the delete kept the lock"
    );
}
