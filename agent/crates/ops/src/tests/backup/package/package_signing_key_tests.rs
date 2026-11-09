//! The signing key: it signs what the verifier accepts, it refuses what is not an
//! ECDSA P-384 private key, and it never prints.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use aws_lc_rs::rand::SystemRandom;
use aws_lc_rs::signature::{
    ECDSA_P256_SHA256_ASN1_SIGNING, ECDSA_P384_SHA384_ASN1, EcdsaKeyPair, UnparsedPublicKey,
};

use crate::backup::package::key_fingerprint::key_fingerprint;
use crate::backup::test_package::generated_key;

use super::*;

#[test]
fn what_the_key_signs_verifies_under_its_public_key() {
    let key = generated_key();
    let signature = key.sign(b"manifest").unwrap();

    UnparsedPublicKey::new(&ECDSA_P384_SHA384_ASN1, key.public_key())
        .verify(b"manifest", &signature)
        .unwrap();
    assert!(
        UnparsedPublicKey::new(&ECDSA_P384_SHA384_ASN1, key.public_key())
            .verify(b"another manifest", &signature)
            .is_err(),
        "inverse control: the same signature does not verify another message"
    );
}

#[test]
fn the_fingerprint_is_the_sha256_of_the_public_point() {
    let key = generated_key();

    assert_eq!(key.fingerprint(), key_fingerprint(&key.public_key()));
    assert_eq!(key.public_key().len(), 97);
    assert_eq!(key.fingerprint().len(), 64);
}

#[test]
fn bytes_that_are_not_a_pkcs8_document_are_rejected() {
    assert_eq!(
        PackageSigningKey::from_pkcs8(b"not a key").map(|_| ()),
        Err(BackupError::PackageSigningKeyRejected)
    );
    assert_eq!(
        PackageSigningKey::from_pkcs8(&[]).map(|_| ()),
        Err(BackupError::PackageSigningKeyRejected)
    );
}

#[test]
fn a_key_on_another_curve_is_rejected() {
    let document =
        EcdsaKeyPair::generate_pkcs8(&ECDSA_P256_SHA256_ASN1_SIGNING, &SystemRandom::new())
            .unwrap();

    assert_eq!(
        PackageSigningKey::from_pkcs8(document.as_ref()).map(|_| ()),
        Err(BackupError::PackageSigningKeyRejected)
    );
}

#[test]
fn the_key_prints_as_redacted() {
    assert_eq!(format!("{:?}", generated_key()), "«redacted»");
}
