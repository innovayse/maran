//! The private key an exporting host signs a package with.

use core::fmt;

use aws_lc_rs::rand::SystemRandom;
use aws_lc_rs::signature::{ECDSA_P384_SHA384_ASN1_SIGNING, EcdsaKeyPair, KeyPair as _};

use crate::backup::BackupError;
use crate::backup::package::key_fingerprint::key_fingerprint;

/// An ECDSA P-384 key pair, parsed from the PKCS#8 document a request carried.
///
/// The private half is held by the crypto library for the life of the value and
/// freed with it; the type has no accessor for it, and its `Debug` prints
/// «redacted», so it cannot reach a log line through a derive on a request struct.
pub struct PackageSigningKey {
    /// The parsed pair.
    pair: EcdsaKeyPair,
}

impl PackageSigningKey {
    /// Parses an unencrypted PKCS#8 (RFC 5208) ECDSA P-384 private key.
    ///
    /// # Errors
    ///
    /// [`BackupError::PackageSigningKeyRejected`] for anything else: another
    /// curve, another encoding, trailing bytes, a key that fails the library's
    /// consistency check.
    pub fn from_pkcs8(document: &[u8]) -> Result<Self, BackupError> {
        EcdsaKeyPair::from_pkcs8(&ECDSA_P384_SHA384_ASN1_SIGNING, document)
            .map(|pair| Self { pair })
            .map_err(|_| BackupError::PackageSigningKeyRejected)
    }

    /// The public key as an uncompressed SEC1 point.
    #[must_use]
    pub fn public_key(&self) -> Vec<u8> {
        self.pair.public_key().as_ref().to_vec()
    }

    /// The fingerprint of [`Self::public_key`].
    #[must_use]
    pub fn fingerprint(&self) -> String {
        key_fingerprint(self.pair.public_key().as_ref())
    }

    /// Signs `message` with ECDSA P-384 over SHA-384; the signature is ASN.1 DER.
    ///
    /// # Errors
    ///
    /// [`BackupError::PackageSigningKeyRejected`] when the library cannot sign,
    /// which for a key it accepted at parse time is a fault of the host.
    pub fn sign(&self, message: &[u8]) -> Result<Vec<u8>, BackupError> {
        let rng = SystemRandom::new();
        self.pair
            .sign(&rng, message)
            .map(|signature| signature.as_ref().to_vec())
            .map_err(|_| BackupError::PackageSigningKeyRejected)
    }
}

impl fmt::Debug for PackageSigningKey {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str("«redacted»")
    }
}

#[cfg(test)]
#[path = "../../tests/backup/package/package_signing_key_tests.rs"]
mod tests;
