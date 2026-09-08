//! The inode's answer about the directory an artifact is written into.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{DirBuilder, Permissions, create_dir, set_permissions, write};
use std::os::unix::fs::{PermissionsExt as _, symlink};

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use super::*;

/// An account name for these tests.
fn account() -> AccountName {
    AccountName::parse("alice").expect("the fixture name is valid")
}

/// The uid the fixtures are really owned by, since a test is not root.
fn owner() -> u32 {
    current_uid().expect("the current uid is readable")
}

/// A root-only directory a test can really create.
fn root_only_directory() -> TempDir {
    let directory = TempDir::new().expect("a temporary directory");
    set_permissions(directory.path(), Permissions::from_mode(0o700))
        .expect("the fixture is chmodable");
    directory
}

/// **The inverse control.** A directory that is what it should be is ACCEPTED,
/// and the account's own directory is created under it, root-only.
#[test]
fn a_root_only_directory_is_accepted_and_the_account_directory_is_created() {
    let root = root_only_directory();

    let directory = prepare_account_directory_owned_by(root.path(), &account(), owner())
        .expect("a root-only directory is usable");

    assert_eq!(directory, root.path().join("alice"));
    let mode = directory
        .symlink_metadata()
        .expect("the directory was created")
        .mode()
        & 0o7777;
    assert_eq!(mode, 0o700, "created 0{mode:o}");
}

/// A directory somebody else owns is refused, whatever its mode says.
#[test]
fn a_backup_root_owned_by_another_user_is_refused() {
    let root = root_only_directory();

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner() + 1);

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}

/// A group-readable root is refused: the string check said nothing about this.
#[test]
fn a_backup_root_the_group_can_read_is_refused() {
    let root = root_only_directory();
    set_permissions(root.path(), Permissions::from_mode(0o750)).expect("chmod");

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(
        refusal,
        Err(BackupError::BackupRootUnsafe { mode: 0o750, .. })
    ));
}

/// A world-writable root is refused — the case that lets somebody else choose
/// the artifact a later restore reads.
#[test]
fn a_world_writable_backup_root_is_refused() {
    let root = root_only_directory();
    set_permissions(root.path(), Permissions::from_mode(0o707)).expect("chmod");

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}

/// The setgid bit is refused too: it is inherited by everything written below.
#[test]
fn a_setgid_backup_root_is_refused() {
    let root = root_only_directory();
    set_permissions(root.path(), Permissions::from_mode(0o2700)).expect("chmod");

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}

/// **The account's own directory is checked, not only its parent.** A directory
/// left behind with a drifted mode is refused even under a perfect root.
#[test]
fn an_account_directory_whose_mode_drifted_is_refused() {
    let root = root_only_directory();
    let directory = root.path().join("alice");
    DirBuilder::new()
        .mode(0o755)
        .create(&directory)
        .expect("the drifted directory");

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(
        refusal,
        Err(BackupError::BackupRootUnsafe { mode: 0o755, .. })
    ));
}

/// A symlink where the account's directory should be is refused rather than
/// followed — the write would land wherever its author last pointed it.
#[test]
fn a_symlink_where_the_account_directory_should_be_is_refused() {
    let root = root_only_directory();
    let elsewhere = TempDir::new().expect("a temporary directory");
    set_permissions(elsewhere.path(), Permissions::from_mode(0o700)).expect("chmod");
    symlink(elsewhere.path(), root.path().join("alice")).expect("the planted link");

    let refusal = prepare_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}

/// A file where the root should be is not a directory, and is refused.
#[test]
fn a_backup_root_that_is_not_a_directory_is_refused() {
    let parent = root_only_directory();
    let file = parent.path().join("root");
    write(&file, b"not a directory").expect("the fixture file");

    let refusal = prepare_account_directory_owned_by(&file, &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}

/// A root that is not there at all is reported as unusable, not as safe.
#[test]
fn a_backup_root_that_does_not_exist_is_unusable() {
    let parent = root_only_directory();

    let refusal =
        prepare_account_directory_owned_by(&parent.path().join("missing"), &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnusable)));
}

/// An account directory that already exists and is correct is reused rather
/// than refused: creating a backup twice must converge.
#[test]
fn an_existing_root_only_account_directory_is_reused() {
    let root = root_only_directory();
    create_dir(root.path().join("alice")).expect("the existing directory");
    set_permissions(root.path().join("alice"), Permissions::from_mode(0o700)).expect("chmod");

    let directory = prepare_account_directory_owned_by(root.path(), &account(), owner())
        .expect("an existing root-only directory is usable");

    assert_eq!(directory, root.path().join("alice"));
}

/// **F7.** An account with no directory under the root answers `None` and —
/// the whole point — the directory is NOT created. A read or a delete that
/// leaves an inode behind for an account it found nothing for is a converged
/// outcome that changed the filesystem, and it accumulates one empty
/// directory per account anybody ever asked about.
#[test]
fn opening_an_absent_account_directory_creates_nothing() {
    let root = root_only_directory();

    let outcome = open_account_directory_owned_by(root.path(), &account(), owner());

    assert_eq!(outcome, Ok(None));
    assert!(
        root.path().join("alice").symlink_metadata().is_err(),
        "the directory was created"
    );
}

/// **The inverse control.** An account directory that exists and is what it
/// should be is answered, so the test above is not passing because the
/// function answers `None` to everything.
#[test]
fn opening_an_existing_account_directory_answers_it() {
    let root = root_only_directory();
    let directory = root.path().join("alice");
    DirBuilder::new()
        .mode(0o700)
        .create(&directory)
        .expect("the fixture directory is creatable");

    let outcome = open_account_directory_owned_by(root.path(), &account(), owner());

    assert_eq!(outcome, Ok(Some(directory)));
}

/// An existing account directory anybody but its owner can reach is refused
/// rather than answered — the same question the creating path asks, asked of
/// a directory this call did not make.
#[test]
fn opening_a_group_readable_account_directory_is_refused() {
    let root = root_only_directory();
    let directory = root.path().join("alice");
    DirBuilder::new()
        .mode(0o750)
        .create(&directory)
        .expect("the fixture directory is creatable");

    let refusal = open_account_directory_owned_by(root.path(), &account(), owner());

    assert!(matches!(refusal, Err(BackupError::BackupRootUnsafe { .. })));
}
