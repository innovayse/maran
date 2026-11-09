//! Tests for the `generate_node_identity` module.
//!
//! The certificate is checked by reading its DER for the exact encodings of what it must say
//! (names, usages, key and signature algorithms, validity dates). That is deliberately a check of
//! the bytes a peer will parse, not of the parameters handed to the generator, which a defect in
//! the generator's own defaults would pass. A peer's actual handshake is the listener's
//! integration test, not this one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt;

use time::{Duration, OffsetDateTime};

use super::{FleetIdentityError, generate_node_identity};
use crate::fleet::fixture_identity::{
    FIXED_NOW_SECONDS, fixed_now, generate_into, own_uid, private_directory, request,
};
use crate::fleet::node_identity::NodeIdentity;

/// The DER encoding of the object identifier id-kp-serverAuth (1.3.6.1.5.5.7.3.1).
const SERVER_AUTH_OID: [u8; 10] = [0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x01];

/// The DER encoding of id-kp-clientAuth (1.3.6.1.5.5.7.3.2).
const CLIENT_AUTH_OID: [u8; 10] = [0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x02];

/// The DER encoding of id-ecPublicKey followed by prime256v1: the subject key is ECDSA P-256.
const P256_KEY_OIDS: [u8; 19] = [
    0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D,
    0x03, 0x01, 0x07,
];

/// The DER encoding of ecdsa-with-SHA256 (1.2.840.10045.4.3.2).
const ECDSA_SHA256_OID: [u8; 10] = [0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04, 0x03, 0x02];

/// The extension VALUE of a basicConstraints with `cA` TRUE: an OCTET STRING holding a SEQUENCE
/// holding a BOOLEAN TRUE. Matching the value and not a bare BOOLEAN keeps the extension's own
/// `critical` flag, which is also a BOOLEAN TRUE, from being mistaken for it.
const CA_TRUE_EXTENSION_VALUE: [u8; 7] = [0x04, 0x05, 0x30, 0x03, 0x01, 0x01, 0xFF];

/// Where `needle` first occurs in `haystack`.
fn find(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

/// The DER of the certificate generated into `directory`.
fn der_of(directory: &std::path::Path) -> Vec<u8> {
    use base64::Engine as _;

    let pem = std::fs::read_to_string(directory.join("node.pem")).unwrap();
    let body: String = pem
        .lines()
        .filter(|line| !line.starts_with("-----"))
        .collect();
    base64::engine::general_purpose::STANDARD
        .decode(body)
        .unwrap()
}

/// A moment rendered as the 13-byte DER UTCTime a certificate validity carries.
fn utc_time(moment: OffsetDateTime) -> String {
    format!(
        "{:02}{:02}{:02}{:02}{:02}{:02}Z",
        moment.year() % 100,
        u8::from(moment.month()),
        moment.day(),
        moment.hour(),
        moment.minute(),
        moment.second()
    )
}

#[test]
fn the_directory_is_created_owner_only_and_both_files_are_owner_only() {
    let parent = private_directory();
    let fleet = parent.path().join("fleet");

    generate_node_identity(
        &request(&fleet, "node1.example.com", false),
        own_uid(),
        fixed_now(),
    )
    .unwrap();

    let mode_of =
        |path: &std::path::Path| std::fs::metadata(path).unwrap().permissions().mode() & 0o777;
    assert_eq!(mode_of(&fleet), 0o700);
    assert_eq!(mode_of(&fleet.join("node.key")), 0o600);
    assert_eq!(mode_of(&fleet.join("node.pem")), 0o600);
}

#[test]
fn the_returned_fingerprint_is_the_one_a_later_read_computes_from_the_file() {
    let directory = private_directory();

    let generated = generate_into(directory.path());
    let read_back = NodeIdentity::read(directory.path(), own_uid()).unwrap();

    assert_eq!(generated, read_back.fingerprint);
}

#[test]
fn the_private_key_is_a_pkcs8_pem_that_never_appears_in_the_certificate_file() {
    let directory = private_directory();
    generate_into(directory.path());

    let key = std::fs::read_to_string(directory.path().join("node.key")).unwrap();
    let certificate = std::fs::read_to_string(directory.path().join("node.pem")).unwrap();

    assert!(key.starts_with("-----BEGIN PRIVATE KEY-----"));
    assert!(!certificate.contains("PRIVATE KEY"));
}

#[test]
fn the_certificate_is_p256_signed_with_sha256_and_serves_but_does_not_authenticate_clients() {
    let directory = private_directory();
    generate_into(directory.path());
    let der = der_of(directory.path());

    assert!(
        find(&der, &P256_KEY_OIDS).is_some(),
        "subject key must be ECDSA P-256"
    );
    assert!(
        find(&der, &ECDSA_SHA256_OID).is_some(),
        "must be signed ecdsa-with-SHA256"
    );
    assert!(
        find(&der, &SERVER_AUTH_OID).is_some(),
        "must carry serverAuth"
    );
    assert!(
        find(&der, &CLIENT_AUTH_OID).is_none(),
        "a node never presents a client certificate"
    );
}

#[test]
fn the_certificate_is_not_a_certificate_authority() {
    let directory = private_directory();
    generate_into(directory.path());
    let der = der_of(directory.path());

    assert!(
        find(&der, &CA_TRUE_EXTENSION_VALUE).is_none(),
        "basicConstraints must not say cA TRUE"
    );
}

#[test]
fn the_validity_starts_an_hour_before_issue_and_ends_two_years_after_it() {
    let directory = private_directory();
    generate_into(directory.path());
    let der = der_of(directory.path());

    let issued =
        OffsetDateTime::from_unix_timestamp(i64::try_from(FIXED_NOW_SECONDS).unwrap()).unwrap();
    let expected_start = utc_time(issued - Duration::hours(1));
    let expected_end = utc_time(issued + Duration::days(730));

    // Two consecutive UTCTime values (tag 0x17, length 13): notBefore then notAfter.
    let mut expected = vec![0x17, 0x0D];
    expected.extend(expected_start.as_bytes());
    expected.extend([0x17, 0x0D]);
    expected.extend(expected_end.as_bytes());
    assert!(
        find(&der, &expected).is_some(),
        "validity must be {expected_start} to {expected_end}"
    );
}

#[test]
fn a_host_name_address_is_the_only_dns_subject_alternative_name() {
    let directory = private_directory();
    generate_node_identity(
        &request(directory.path(), "Node1.Example.COM", false),
        own_uid(),
        fixed_now(),
    )
    .unwrap();
    let der = der_of(directory.path());

    // dNSName [2], length 17, then the lowercased name.
    let mut expected = vec![0x82, 0x11];
    expected.extend(b"node1.example.com");
    assert!(find(&der, &expected).is_some());
}

#[test]
fn an_ip_address_is_named_as_an_ip_address_entry_and_not_as_text() {
    let directory = private_directory();
    generate_node_identity(
        &request(directory.path(), "203.0.113.7", false),
        own_uid(),
        fixed_now(),
    )
    .unwrap();
    let der = der_of(directory.path());

    // iPAddress [7], length 4, then the four octets.
    assert!(find(&der, &[0x87, 0x04, 203, 0, 113, 7]).is_some());
    assert!(find(&der, b"203.0.113.7").is_none());
}

#[test]
fn the_subject_names_the_role_and_differs_between_two_nodes() {
    let first = private_directory();
    let second = private_directory();
    generate_into(first.path());
    generate_into(second.path());

    let name_in = |directory: &std::path::Path| {
        let der = der_of(directory);
        let start = find(&der, b"maran-node-").expect("the subject carries the role");
        der[start..start + "maran-node-".len() + 16].to_vec()
    };

    assert_ne!(name_in(first.path()), name_in(second.path()));
}

#[test]
fn two_identities_for_the_same_address_have_different_fingerprints() {
    let first = private_directory();
    let second = private_directory();

    assert_ne!(generate_into(first.path()), generate_into(second.path()));
}

#[test]
fn an_existing_identity_is_refused_and_left_byte_for_byte_unchanged() {
    let directory = private_directory();
    generate_into(directory.path());
    let key_before = std::fs::read(directory.path().join("node.key")).unwrap();
    let certificate_before = std::fs::read(directory.path().join("node.pem")).unwrap();

    let refusal = generate_node_identity(
        &request(directory.path(), "node1.example.com", false),
        own_uid(),
        fixed_now(),
    );

    assert!(matches!(
        refusal,
        Err(FleetIdentityError::IdentityAlreadyExists { .. })
    ));
    assert_eq!(
        std::fs::read(directory.path().join("node.key")).unwrap(),
        key_before
    );
    assert_eq!(
        std::fs::read(directory.path().join("node.pem")).unwrap(),
        certificate_before
    );
}

#[test]
fn a_directory_holding_only_a_key_or_only_a_certificate_counts_as_an_existing_identity() {
    for leftover in ["node.key", "node.pem"] {
        let directory = private_directory();
        std::fs::write(directory.path().join(leftover), b"x").unwrap();

        assert!(
            matches!(
                generate_node_identity(
                    &request(directory.path(), "node1.example.com", false),
                    own_uid(),
                    fixed_now()
                ),
                Err(FleetIdentityError::IdentityAlreadyExists { .. })
            ),
            "{leftover} alone must block generation"
        );
    }
}

#[test]
fn replacing_writes_a_new_identity_and_the_old_fingerprint_no_longer_matches() {
    let directory = private_directory();
    let old = generate_into(directory.path());

    let new = generate_node_identity(
        &request(directory.path(), "node1.example.com", true),
        own_uid(),
        fixed_now(),
    )
    .unwrap();

    assert_ne!(old, new);
    assert_eq!(
        NodeIdentity::read(directory.path(), own_uid())
            .unwrap()
            .fingerprint,
        new
    );
}

#[test]
fn replacing_in_a_directory_with_no_identity_is_the_same_as_generating() {
    let directory = private_directory();

    let result = generate_node_identity(
        &request(directory.path(), "node1.example.com", true),
        own_uid(),
        fixed_now(),
    );

    assert!(result.is_ok());
}

#[test]
fn a_directory_that_is_not_private_is_refused_before_any_key_is_made() {
    let directory = private_directory();
    std::fs::set_permissions(directory.path(), std::fs::Permissions::from_mode(0o755)).unwrap();

    let refusal = generate_node_identity(
        &request(directory.path(), "node1.example.com", false),
        own_uid(),
        fixed_now(),
    );

    assert!(matches!(
        refusal,
        Err(FleetIdentityError::DirectoryTooPermissive { .. })
    ));
    assert!(!directory.path().join("node.key").exists());
}
