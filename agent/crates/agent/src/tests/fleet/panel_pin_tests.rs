//! Tests for the `panel_pin` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use super::{FleetIdentityError, PanelPin};
use crate::fleet::fixture_identity::{generate_into, own_uid, private_directory};

/// Puts a real, valid certificate at `panel.pem`: the node's own, which stands in for a panel's.
/// Returns its fingerprint.
fn pin_a_certificate(
    directory: &std::path::Path,
) -> maran_agent_core::validation::fleet::certificate_fingerprint::CertificateFingerprint {
    let fingerprint = generate_into(directory);
    std::fs::rename(directory.join("node.pem"), directory.join("panel.pem")).unwrap();
    fingerprint
}

#[test]
fn the_pinned_certificate_is_read_with_its_fingerprint() {
    let directory = private_directory();
    let fingerprint = pin_a_certificate(directory.path());

    let pin = PanelPin::read(directory.path(), own_uid()).unwrap();

    assert_eq!(pin.fingerprint, fingerprint);
    assert!(
        pin.certificate_pem
            .starts_with("-----BEGIN CERTIFICATE-----")
    );
}

#[test]
fn a_node_that_has_not_been_enrolled_has_no_pin() {
    let directory = private_directory();

    assert!(matches!(
        PanelPin::read(directory.path(), own_uid()),
        Err(FleetIdentityError::FileMissing { path }) if path.ends_with("panel.pem")
    ));
}

#[test]
fn removing_the_pin_file_is_seen_by_the_very_next_read() {
    let directory = private_directory();
    pin_a_certificate(directory.path());
    assert!(PanelPin::read(directory.path(), own_uid()).is_ok());

    std::fs::remove_file(directory.path().join("panel.pem")).unwrap();

    assert!(matches!(
        PanelPin::read(directory.path(), own_uid()),
        Err(FleetIdentityError::FileMissing { .. })
    ));
}

#[test]
fn replacing_the_pinned_certificate_changes_the_fingerprint_the_next_read_returns() {
    let directory = private_directory();
    let first = pin_a_certificate(directory.path());
    std::fs::remove_file(directory.path().join("node.key")).unwrap();

    let second = pin_a_certificate(directory.path());

    assert_ne!(first, second);
    assert_eq!(
        PanelPin::read(directory.path(), own_uid())
            .unwrap()
            .fingerprint,
        second
    );
}

#[test]
fn a_pin_file_that_is_not_one_certificate_is_refused() {
    let directory = private_directory();
    std::fs::write(directory.path().join("panel.pem"), "not a certificate\n").unwrap();

    assert!(matches!(
        PanelPin::read(directory.path(), own_uid()),
        Err(FleetIdentityError::CertificateMalformed { .. })
    ));
}

#[test]
fn a_pin_file_that_is_not_text_is_refused() {
    let directory = private_directory();
    std::fs::write(directory.path().join("panel.pem"), [0xFF_u8, 0xFE]).unwrap();

    assert!(matches!(
        PanelPin::read(directory.path(), own_uid()),
        Err(FleetIdentityError::CertificateMalformed { .. })
    ));
}

#[test]
fn a_directory_others_can_enter_is_refused_so_nobody_else_can_swap_the_pin() {
    let directory = private_directory();
    pin_a_certificate(directory.path());
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o750)).unwrap();

    assert!(matches!(
        PanelPin::read(directory.path(), own_uid()),
        Err(FleetIdentityError::DirectoryTooPermissive { .. })
    ));
}
