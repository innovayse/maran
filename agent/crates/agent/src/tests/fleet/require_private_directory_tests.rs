//! Tests for the `require_private_directory` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use super::{FleetIdentityError, require_private_directory};
use crate::fleet::fixture_identity::{own_uid, private_directory};

/// Sets `directory`'s permission bits.
fn chmod(directory: &std::path::Path, mode: u32) {
    std::fs::set_permissions(directory, std::fs::Permissions::from_mode(mode)).unwrap();
}

#[test]
fn a_directory_only_its_owner_can_enter_is_accepted() {
    let directory = private_directory();
    chmod(directory.path(), 0o700);

    assert!(require_private_directory(directory.path(), own_uid()).is_ok());
}

#[test]
fn a_directory_readable_by_its_group_or_by_everyone_is_refused_with_its_mode() {
    let directory = private_directory();

    for mode in [0o750, 0o755, 0o701, 0o770, 0o777, 0o710] {
        chmod(directory.path(), mode);

        match require_private_directory(directory.path(), own_uid()) {
            Err(FleetIdentityError::DirectoryTooPermissive { mode: seen, .. }) => {
                assert_eq!(seen, mode);
            }
            other => panic!("mode {mode:o} must be refused as too permissive, got {other:?}"),
        }
    }
}

#[test]
fn a_directory_owned_by_another_uid_is_refused() {
    let directory = private_directory();
    let someone_else = own_uid().wrapping_add(1);

    match require_private_directory(directory.path(), someone_else) {
        Err(FleetIdentityError::DirectoryOwnedByAnother {
            owner, expected, ..
        }) => {
            assert_eq!(owner, own_uid());
            assert_eq!(expected, someone_else);
        }
        other => panic!("another owner must be refused, got {other:?}"),
    }
}

#[test]
fn a_directory_that_is_not_there_is_reported_missing() {
    let directory = private_directory();
    let absent = directory.path().join("absent");

    assert!(matches!(
        require_private_directory(&absent, own_uid()),
        Err(FleetIdentityError::DirectoryMissing { .. })
    ));
}

#[test]
fn a_plain_file_is_not_a_directory() {
    let directory = private_directory();
    let file = directory.path().join("file");
    std::fs::write(&file, b"x").unwrap();

    assert!(matches!(
        require_private_directory(&file, own_uid()),
        Err(FleetIdentityError::NotADirectory { .. })
    ));
}

#[test]
fn a_symbolic_link_to_a_private_directory_is_refused_rather_than_followed() {
    let directory = private_directory();
    let target = directory.path().join("target");
    std::fs::create_dir(&target).unwrap();
    chmod(&target, 0o700);
    let link = directory.path().join("link");
    std::os::unix::fs::symlink(&target, &link).unwrap();

    // The control: the target itself is accepted, so the refusal below is the link's doing.
    assert!(require_private_directory(&target, own_uid()).is_ok());
    assert!(matches!(
        require_private_directory(&link, own_uid()),
        Err(FleetIdentityError::NotADirectory { .. })
    ));
}
