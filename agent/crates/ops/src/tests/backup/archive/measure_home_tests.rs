//! The home is measured the way the archiver will read it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::{Permissions, create_dir, set_permissions, write};
use std::os::unix::fs::{PermissionsExt as _, symlink};

use tempfile::TempDir;

use super::*;

/// Every ordinary file under the home is counted.
#[test]
fn every_file_under_the_home_is_counted() {
    let home = TempDir::new().expect("a temporary directory");
    write(home.path().join("a"), b"1234").expect("the fixture file");
    create_dir(home.path().join("sub")).expect("the fixture directory");
    write(home.path().join("sub").join("b"), b"12345").expect("the fixture file");

    let bytes = measure_home_bytes(home.path()).expect("the home is readable");

    assert_eq!(bytes, 9);
}

/// **A symlink is counted as the link it is and is never followed.** A home
/// whose `secrets` points at `/etc/shadow` must not report that file's size,
/// for the same reason the archive must not contain its contents.
#[test]
fn a_symlink_is_counted_as_a_link_and_never_followed() {
    let home = TempDir::new().expect("a temporary directory");
    let elsewhere = TempDir::new().expect("a temporary directory");
    write(elsewhere.path().join("big"), vec![b'x'; 4096]).expect("the fixture file");
    symlink(elsewhere.path().join("big"), home.path().join("secrets")).expect("the planted link");

    let bytes = measure_home_bytes(home.path()).expect("the home is readable");

    assert!(bytes < 4096, "the link's target was followed: {bytes}");
}

/// A symlink to a DIRECTORY is not descended into either.
#[test]
fn a_symlink_to_a_directory_is_not_descended_into() {
    let home = TempDir::new().expect("a temporary directory");
    let elsewhere = TempDir::new().expect("a temporary directory");
    write(elsewhere.path().join("big"), vec![b'x'; 4096]).expect("the fixture file");
    symlink(elsewhere.path(), home.path().join("etc")).expect("the planted link");

    let bytes = measure_home_bytes(home.path()).expect("the home is readable");

    assert!(bytes < 4096, "the linked directory was walked: {bytes}");
}

/// A subtree that cannot be read is a failure, not a smaller home.
#[test]
fn an_unreadable_subtree_is_a_failure_and_not_a_zero() {
    if maran_agent_core::utils::current_uid::current_uid().unwrap_or(0) == 0 {
        // Root is not refused by a mode, so this run cannot observe the
        // behaviour at all. Said out loud rather than passing quietly.
        eprintln!("UNOBSERVED HERE: running as root, a 0o000 directory is readable");
        return;
    }

    let home = TempDir::new().expect("a temporary directory");
    let closed = home.path().join("closed");
    create_dir(&closed).expect("the fixture directory");
    write(closed.join("a"), b"1234").expect("the fixture file");
    set_permissions(&closed, Permissions::from_mode(0o000)).expect("chmod");

    let refusal = measure_home_bytes(home.path());
    set_permissions(&closed, Permissions::from_mode(0o700)).expect("chmod back");

    assert!(matches!(refusal, Err(BackupError::HomeUnreadable)));
}

/// A home that is not there is refused rather than measured as empty.
#[test]
fn a_home_that_is_not_there_is_refused() {
    let parent = TempDir::new().expect("a temporary directory");

    let refusal = measure_home_bytes(&parent.path().join("absent"));

    assert!(matches!(refusal, Err(BackupError::HomeUnreadable)));
}
