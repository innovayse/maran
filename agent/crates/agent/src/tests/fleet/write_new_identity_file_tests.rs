//! Tests for the `write_new_identity_file` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use super::{FleetIdentityError, write_new_identity_file};
use crate::fleet::fixture_identity::private_directory;

#[test]
fn a_new_file_holds_the_contents_and_exactly_the_requested_mode() {
    let directory = private_directory();
    let path = directory.path().join("node.key");

    write_new_identity_file(&path, b"key material", 0o600).unwrap();

    assert_eq!(std::fs::read(&path).unwrap(), b"key material");
    let mode = std::fs::metadata(&path).unwrap().permissions().mode() & 0o777;
    assert_eq!(mode, 0o600);
}

#[test]
fn a_file_that_already_exists_is_never_overwritten() {
    let directory = private_directory();
    let path = directory.path().join("node.key");
    std::fs::write(&path, b"old").unwrap();

    match write_new_identity_file(&path, b"new", 0o600) {
        Err(FleetIdentityError::Io { source, .. }) => {
            assert_eq!(source.kind(), std::io::ErrorKind::AlreadyExists);
        }
        other => panic!("an existing file must be refused, got {other:?}"),
    }
    assert_eq!(std::fs::read(&path).unwrap(), b"old");
}

#[test]
fn a_planted_symbolic_link_is_not_written_through() {
    let directory = private_directory();
    let target = directory.path().join("victim");
    std::fs::write(&target, b"untouched").unwrap();
    let path = directory.path().join("node.key");
    std::os::unix::fs::symlink(&target, &path).unwrap();

    assert!(write_new_identity_file(&path, b"key material", 0o600).is_err());
    assert_eq!(std::fs::read(&target).unwrap(), b"untouched");
}
