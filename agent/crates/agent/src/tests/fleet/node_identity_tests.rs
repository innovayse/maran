//! Tests for the `node_identity` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use super::{FleetIdentityError, NodeIdentity};
use crate::fleet::fixture_identity::{generate_into, own_uid, private_directory};

#[test]
fn a_generated_identity_reads_back_with_its_key_certificate_and_fingerprint() {
    let directory = private_directory();
    let fingerprint = generate_into(directory.path());

    let identity = NodeIdentity::read(directory.path(), own_uid()).unwrap();

    assert_eq!(identity.fingerprint, fingerprint);
    assert!(
        identity
            .certificate_pem
            .starts_with("-----BEGIN CERTIFICATE-----")
    );
    assert!(
        identity
            .private_key_pem
            .expose()
            .starts_with("-----BEGIN PRIVATE KEY-----")
    );
}

#[test]
fn printing_the_identity_never_prints_the_private_key() {
    let directory = private_directory();
    generate_into(directory.path());
    let identity = NodeIdentity::read(directory.path(), own_uid()).unwrap();
    let key = identity.private_key_pem.expose().to_owned();
    let body = key.lines().nth(1).unwrap();

    let printed = format!("{identity:?}");

    assert!(!printed.contains(body));
    assert!(printed.contains("redacted"));
}

#[test]
fn a_directory_readable_by_others_is_refused_even_when_the_files_are_there() {
    let directory = private_directory();
    generate_into(directory.path());
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o755)).unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::DirectoryTooPermissive { .. })
    ));
}

#[test]
fn a_directory_with_no_identity_names_the_missing_key() {
    let directory = private_directory();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::FileMissing { path }) if path.ends_with("node.key")
    ));
}

#[test]
fn a_key_without_its_certificate_names_the_missing_certificate() {
    let directory = private_directory();
    generate_into(directory.path());
    std::fs::remove_file(directory.path().join("node.pem")).unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::FileMissing { path }) if path.ends_with("node.pem")
    ));
}

#[test]
fn a_key_file_that_is_not_a_private_key_is_refused() {
    let directory = private_directory();
    generate_into(directory.path());
    std::fs::write(directory.path().join("node.key"), "just some text\n").unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::KeyMalformed { .. })
    ));
}

#[test]
fn a_key_file_that_is_not_text_is_refused() {
    let directory = private_directory();
    generate_into(directory.path());
    std::fs::write(directory.path().join("node.key"), [0xFF_u8, 0xFE, 0x00]).unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::KeyMalformed { .. })
    ));
}

#[test]
fn a_certificate_file_with_two_certificates_is_refused() {
    let directory = private_directory();
    generate_into(directory.path());
    let path = directory.path().join("node.pem");
    let one = std::fs::read_to_string(&path).unwrap();
    std::fs::write(&path, format!("{one}{one}")).unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::CertificateMalformed { .. })
    ));
}

#[test]
fn a_certificate_file_that_is_not_text_is_refused() {
    let directory = private_directory();
    generate_into(directory.path());
    std::fs::write(directory.path().join("node.pem"), [0xFF_u8, 0xFE]).unwrap();

    assert!(matches!(
        NodeIdentity::read(directory.path(), own_uid()),
        Err(FleetIdentityError::CertificateMalformed { .. })
    ));
}
