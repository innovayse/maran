//! Tests for the `certificate_fingerprint` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CertificateFingerprint, CertificateFingerprintError};

/// SHA-256 of the three bytes `abc`, the standard test vector (FIPS 180-2 appendix B.1).
const ABC_SHA256: &str = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

#[test]
fn the_fingerprint_of_known_bytes_is_the_published_sha256_in_lowercase_hex() {
    assert_eq!(CertificateFingerprint::of_der(b"abc").as_str(), ABC_SHA256);
}

#[test]
fn a_computed_fingerprint_is_sixty_four_lowercase_hex_characters() {
    let fingerprint = CertificateFingerprint::of_der(&[0_u8, 1, 2, 3]);

    assert_eq!(fingerprint.as_str().len(), 64);
    assert!(
        fingerprint
            .as_str()
            .chars()
            .all(|character| matches!(character, '0'..='9' | 'a'..='f'))
    );
}

#[test]
fn a_fingerprint_that_was_typed_back_equals_the_one_that_was_computed() {
    let computed = CertificateFingerprint::of_der(b"abc");
    let typed = CertificateFingerprint::parse(ABC_SHA256).unwrap();

    assert_eq!(computed, typed);
}

#[test]
fn two_different_certificates_are_never_equal() {
    assert_ne!(
        CertificateFingerprint::of_der(b"abc"),
        CertificateFingerprint::of_der(b"abd")
    );
}

#[test]
fn a_difference_in_the_last_character_alone_makes_two_fingerprints_unequal() {
    let mut changed = ABC_SHA256.to_owned();
    changed.pop();
    changed.push('e');

    assert_ne!(
        CertificateFingerprint::parse(ABC_SHA256).unwrap(),
        CertificateFingerprint::parse(&changed).unwrap()
    );
}

#[test]
fn a_candidate_of_the_wrong_length_is_refused_with_its_length() {
    assert_eq!(
        CertificateFingerprint::parse(&ABC_SHA256[..63]),
        Err(CertificateFingerprintError::WrongLength { length: 63 })
    );
    assert_eq!(
        CertificateFingerprint::parse(""),
        Err(CertificateFingerprintError::WrongLength { length: 0 })
    );
}

#[test]
fn upper_case_and_separators_are_refused() {
    assert_eq!(
        CertificateFingerprint::parse(&ABC_SHA256.to_uppercase()),
        Err(CertificateFingerprintError::NotLowercaseHex)
    );

    let mut with_colon = ABC_SHA256.to_owned();
    with_colon.replace_range(0..1, ":");
    assert_eq!(
        CertificateFingerprint::parse(&with_colon),
        Err(CertificateFingerprintError::NotLowercaseHex)
    );
}

#[test]
fn a_non_hex_letter_of_the_right_length_is_refused() {
    let candidate = "g".repeat(64);

    assert_eq!(
        CertificateFingerprint::parse(&candidate),
        Err(CertificateFingerprintError::NotLowercaseHex)
    );
}
