//! Tests for the `read_identity_file` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{FleetIdentityError, read_identity_file};
use crate::fleet::fixture_identity::private_directory;

#[test]
fn a_small_plain_file_is_read_whole() {
    let directory = private_directory();
    let path = directory.path().join("node.pem");
    std::fs::write(&path, b"hello").unwrap();

    assert_eq!(read_identity_file(&path, 16).unwrap(), b"hello");
}

#[test]
fn a_file_exactly_as_large_as_the_limit_is_accepted_and_one_byte_more_is_refused() {
    let directory = private_directory();
    let path = directory.path().join("node.pem");

    std::fs::write(&path, [b'a'; 8]).unwrap();
    assert_eq!(read_identity_file(&path, 8).unwrap().len(), 8);

    std::fs::write(&path, [b'a'; 9]).unwrap();
    assert!(matches!(
        read_identity_file(&path, 8),
        Err(FleetIdentityError::FileTooLarge { limit: 8, .. })
    ));
}

#[test]
fn a_file_that_is_not_there_is_reported_missing() {
    let directory = private_directory();

    assert!(matches!(
        read_identity_file(&directory.path().join("absent"), 8),
        Err(FleetIdentityError::FileMissing { .. })
    ));
}

#[test]
fn a_directory_is_not_a_plain_file() {
    let directory = private_directory();

    assert!(matches!(
        read_identity_file(directory.path(), 8),
        Err(FleetIdentityError::FileNotPlain { .. })
    ));
}

#[test]
fn a_symbolic_link_is_refused_rather_than_followed() {
    let directory = private_directory();
    let target = directory.path().join("target");
    std::fs::write(&target, b"secret").unwrap();
    let link = directory.path().join("link");
    std::os::unix::fs::symlink(&target, &link).unwrap();

    // The control: the target itself reads, so the refusal is the link's doing.
    assert_eq!(read_identity_file(&target, 16).unwrap(), b"secret");
    assert!(matches!(
        read_identity_file(&link, 16),
        Err(FleetIdentityError::FileNotPlain { .. })
    ));
}
