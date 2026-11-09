//! Tests for the `ensure_private_directory` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use super::{FleetIdentityError, ensure_private_directory};
use crate::fleet::fixture_identity::{own_uid, private_directory};

#[test]
fn an_absent_directory_is_created_owner_only() {
    let parent = private_directory();
    let fleet = parent.path().join("fleet");

    ensure_private_directory(&fleet, own_uid()).unwrap();

    let mode = std::fs::metadata(&fleet).unwrap().permissions().mode() & 0o777;
    assert_eq!(mode, 0o700);
}

#[test]
fn a_missing_parent_is_an_error_and_nothing_deep_is_created() {
    let parent = private_directory();
    let deep = parent.path().join("a").join("b");

    assert!(matches!(
        ensure_private_directory(&deep, own_uid()),
        Err(FleetIdentityError::Io { .. })
    ));
    assert!(!parent.path().join("a").exists());
}

#[test]
fn an_existing_private_directory_is_accepted_and_left_alone() {
    let parent = private_directory();
    let fleet = parent.path().join("fleet");
    std::fs::create_dir(&fleet).unwrap();
    std::fs::set_permissions(&fleet, std::fs::Permissions::from_mode(0o700)).unwrap();
    std::fs::write(fleet.join("kept"), b"x").unwrap();

    ensure_private_directory(&fleet, own_uid()).unwrap();

    assert!(fleet.join("kept").exists());
}

#[test]
fn an_existing_loose_directory_is_refused_and_not_tightened() {
    let parent = private_directory();
    let fleet = parent.path().join("fleet");
    std::fs::create_dir(&fleet).unwrap();
    std::fs::set_permissions(&fleet, std::fs::Permissions::from_mode(0o755)).unwrap();

    assert!(matches!(
        ensure_private_directory(&fleet, own_uid()),
        Err(FleetIdentityError::DirectoryTooPermissive { mode: 0o755, .. })
    ));
    let mode = std::fs::metadata(&fleet).unwrap().permissions().mode() & 0o777;
    assert_eq!(mode, 0o755, "a refusal must not change the directory");
}
