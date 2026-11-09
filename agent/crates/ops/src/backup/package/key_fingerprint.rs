//! The fingerprint an operator compares a signing key by.

use sha2::{Digest as _, Sha256};

use crate::backup::package::lowercase_hex::lowercase_hex;

/// The lowercase hexadecimal SHA-256 of a public key's bytes (the uncompressed
/// SEC1 point), 64 characters.
///
/// The one spelling of "which key signed this": shown to the operator on both
/// servers, carried in the signed manifest and in the import header, and compared
/// there.
#[must_use]
pub fn key_fingerprint(public_key: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(public_key);
    lowercase_hex(&hasher.finalize())
}
