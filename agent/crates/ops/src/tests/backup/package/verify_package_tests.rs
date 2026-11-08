//! Verifying a package manifest: an honest one is accepted, and every way of
//! forging or damaging one is a typed refusal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::backup::test_package::{generated_key, manifest_for, signed, signed_bytes};

use super::*;

const ACCOUNT: &str = "alice";
const BACKUP_ID: &str = "3f9a1c2e-5b4d-4a6f-8e1b-2c3d4e5f6a7b";

fn honest(key: &crate::backup::package::package_signing_key::PackageSigningKey) -> SignedManifest {
    signed(
        key,
        &manifest_for(key, ACCOUNT, BACKUP_ID, b"gzip-bytes", &[]),
    )
}

#[test]
fn a_manifest_signed_by_the_confirmed_key_is_accepted() {
    let key = generated_key();
    let package = honest(&key);

    let manifest = verify_package(&package, &key.fingerprint()).unwrap();

    assert_eq!(manifest.account, ACCOUNT);
    assert_eq!(manifest.backup_id, BACKUP_ID);
}

#[test]
fn one_flipped_bit_in_the_manifest_is_refused() {
    let key = generated_key();
    let mut package = honest(&key);
    package.manifest[10] ^= 0x01;

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageSignatureInvalid)
    );
}

#[test]
fn one_flipped_bit_in_the_signature_is_refused() {
    let key = generated_key();
    let mut package = honest(&key);
    let last = package.signature.len() - 1;
    package.signature[last] ^= 0x01;

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageSignatureInvalid)
    );
}

#[test]
fn a_valid_signature_by_another_key_than_the_confirmed_one_is_refused() {
    let signer = generated_key();
    let confirmed = generated_key();
    let package = honest(&signer);

    assert_eq!(
        verify_package(&package, &confirmed.fingerprint()),
        Err(BackupError::PackageFingerprintMismatch)
    );
}

#[test]
fn a_swapped_public_key_with_its_own_fingerprint_does_not_verify_the_old_signature() {
    let signer = generated_key();
    let other = generated_key();
    let mut package = honest(&signer);
    package.public_key = other.public_key();

    assert_eq!(
        verify_package(&package, &other.fingerprint()),
        Err(BackupError::PackageSignatureInvalid)
    );
}

#[test]
fn an_unconfirmed_or_malformed_fingerprint_is_refused() {
    let key = generated_key();
    let package = honest(&key);

    let upper = key.fingerprint().to_uppercase();
    for expected in ["", "abc", upper.as_str()] {
        assert_eq!(
            verify_package(&package, expected),
            Err(BackupError::PackageFingerprintMismatch),
            "{expected}"
        );
    }
}

#[test]
fn a_public_key_of_the_wrong_length_is_refused() {
    let key = generated_key();
    let mut package = honest(&key);
    package.public_key.truncate(60);

    assert_eq!(
        verify_package(&package, &key_fingerprint(&package.public_key)),
        Err(BackupError::PackageSignatureInvalid)
    );
}

#[test]
fn an_empty_or_oversized_signature_is_refused() {
    let key = generated_key();
    let mut package = honest(&key);
    package.signature = Vec::new();
    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageSignatureInvalid)
    );

    package.signature = vec![0x30; 200];
    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageSignatureInvalid)
    );
}

#[test]
fn a_manifest_over_the_bound_is_refused_before_anything_is_verified() {
    let key = generated_key();
    let package = signed_bytes(&key, vec![b' '; MAXIMUM_MANIFEST_BYTES + 1]);

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageManifestMalformed)
    );
}

#[test]
fn a_signed_manifest_with_an_unknown_field_is_refused() {
    let key = generated_key();
    let mut value =
        serde_json::to_value(manifest_for(&key, ACCOUNT, BACKUP_ID, b"x", &[])).unwrap();
    value["invented"] = serde_json::json!(true);
    let package = signed_bytes(&key, serde_json::to_vec(&value).unwrap());

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageManifestMalformed)
    );
}

#[test]
fn a_signed_manifest_with_a_duplicated_field_is_refused() {
    let key = generated_key();
    let text = serde_json::to_string(&manifest_for(&key, ACCOUNT, BACKUP_ID, b"x", &[])).unwrap();
    let duplicated = text.replacen('{', "{\"account\":\"bobby\",", 1);
    let package = signed_bytes(&key, duplicated.into_bytes());

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageManifestMalformed)
    );
}

#[test]
fn a_signed_manifest_of_an_unknown_version_is_refused() {
    let key = generated_key();
    let mut manifest = manifest_for(&key, ACCOUNT, BACKUP_ID, b"x", &[]);
    manifest.version = 2;
    let package = signed(&key, &manifest);

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageManifestMalformed)
    );
}

#[test]
fn a_signed_manifest_naming_another_signer_is_refused() {
    let key = generated_key();
    let mut manifest = manifest_for(&key, ACCOUNT, BACKUP_ID, b"x", &[]);
    manifest.signer_fingerprint = generated_key().fingerprint();
    let package = signed(&key, &manifest);

    assert_eq!(
        verify_package(&package, &key.fingerprint()),
        Err(BackupError::PackageManifestMalformed)
    );
}
