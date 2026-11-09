//! Deciding whether to believe a package's manifest.

use aws_lc_rs::signature::{ECDSA_P384_SHA384_ASN1, UnparsedPublicKey};

use crate::backup::BackupError;
use crate::backup::package::key_fingerprint::key_fingerprint;
use crate::backup::package::signed_manifest::SignedManifest;
use crate::backup::package::transfer_manifest::{
    MAXIMUM_MANIFEST_BYTES, TRANSFER_MANIFEST_VERSION, TransferManifest,
};

/// The length of an uncompressed SEC1 point on P-384: `0x04 || X || Y`.
const P384_POINT_BYTES: usize = 97;

/// The longest DER signature P-384 can produce (a sequence of two 48-byte
/// integers, each possibly padded by one byte, plus the framing).
const MAXIMUM_SIGNATURE_BYTES: usize = 104;

/// The length of a SHA-256 fingerprint written as hexadecimal.
const FINGERPRINT_HEX_LENGTH: usize = 64;

/// Verifies `signed` against the fingerprint the operator confirmed and parses the
/// manifest it carries.
///
/// In this order, each step refusing before the next runs: bounds on every byte
/// string, the key's fingerprint against `expected_fingerprint`, the signature
/// over the exact manifest bytes, and only then the parse. Nothing a peer chose is
/// parsed before it is known to be signed by the key the operator named.
///
/// A valid signature proves only that the holder of the key signed. That the
/// holder is the server the operator believes it is rests on the fingerprint
/// comparison, which is why an import without an expected fingerprint cannot get
/// past the first step.
///
/// # Errors
///
/// - [`BackupError::PackageFingerprintMismatch`] when `expected_fingerprint` is
///   not 64 lowercase hexadecimal characters or is not the key's fingerprint.
/// - [`BackupError::PackageSignatureInvalid`] for a key or signature of the wrong
///   shape, and for a signature that does not verify.
/// - [`BackupError::PackageManifestMalformed`] for a manifest over the bound, one
///   that does not parse, a version this agent does not know, or one naming a
///   signer other than the key that signed it.
pub fn verify_package(
    signed: &SignedManifest,
    expected_fingerprint: &str,
) -> Result<TransferManifest, BackupError> {
    if signed.manifest.len() > MAXIMUM_MANIFEST_BYTES {
        return Err(BackupError::PackageManifestMalformed);
    }
    if signed.public_key.len() != P384_POINT_BYTES
        || signed.signature.is_empty()
        || signed.signature.len() > MAXIMUM_SIGNATURE_BYTES
    {
        return Err(BackupError::PackageSignatureInvalid);
    }

    let fingerprint = key_fingerprint(&signed.public_key);
    if expected_fingerprint.len() != FINGERPRINT_HEX_LENGTH || expected_fingerprint != fingerprint {
        return Err(BackupError::PackageFingerprintMismatch);
    }

    UnparsedPublicKey::new(&ECDSA_P384_SHA384_ASN1, &signed.public_key)
        .verify(&signed.manifest, &signed.signature)
        .map_err(|_| BackupError::PackageSignatureInvalid)?;

    let manifest: TransferManifest = serde_json::from_slice(&signed.manifest)
        .map_err(|_| BackupError::PackageManifestMalformed)?;
    if manifest.version != TRANSFER_MANIFEST_VERSION || manifest.signer_fingerprint != fingerprint {
        return Err(BackupError::PackageManifestMalformed);
    }

    Ok(manifest)
}

#[cfg(test)]
#[path = "../../tests/backup/package/verify_package_tests.rs"]
mod tests;
