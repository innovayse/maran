//! The SHA-256 fingerprint of a certificate, the one value two fleet peers compare.

use sha2::{Digest, Sha256};
use subtle::ConstantTimeEq;

use super::certificate_fingerprint_error::CertificateFingerprintError;

/// How many characters a fingerprint is: 32 bytes, two hex digits each.
const HEX_LENGTH: usize = 64;

/// The SHA-256 of a certificate's DER encoding, as 64 lowercase hex characters.
///
/// Fleet peers trust each other by this value and by nothing else: there is no certificate
/// authority, so "is this the certificate I was told about" is a comparison of two fingerprints.
/// That comparison is the security decision, so it is [`PartialEq`] here and it is constant-time:
/// an equality that returned at the first differing byte would tell a peer who can try many
/// certificates how many leading bytes of the pin it had matched.
///
/// The only constructors are [`CertificateFingerprint::of_der`], which computes one, and
/// [`CertificateFingerprint::parse`], which reads one an operator typed; holding a value is the
/// proof that it has the shape. It is public, not secret, and prints as itself.
#[derive(Debug, Clone)]
pub struct CertificateFingerprint(String);

impl CertificateFingerprint {
    /// The fingerprint of `der`, which is the certificate's DER encoding and not its PEM text.
    ///
    /// Hashing the DER and not the PEM is what makes two renderings of one certificate (line
    /// width, a trailing newline, a comment above the block) the same fingerprint.
    #[must_use]
    pub fn of_der(der: &[u8]) -> Self {
        let digest = Sha256::digest(der);
        let mut rendered = String::with_capacity(HEX_LENGTH);
        for byte in digest.iter() {
            rendered.push_str(&format!("{byte:02x}"));
        }
        Self(rendered)
    }

    /// Reads a fingerprint someone typed or a file carried.
    ///
    /// Surrounding whitespace is not stripped: a caller that wants it tolerated trims first and
    /// says so, because a value that silently changes on the way in is not the value that was
    /// checked.
    ///
    /// # Errors
    ///
    /// - [`CertificateFingerprintError::WrongLength`] when `candidate` is not 64 characters.
    /// - [`CertificateFingerprintError::NotLowercaseHex`] when any character is outside `0-9a-f`.
    pub fn parse(candidate: &str) -> Result<Self, CertificateFingerprintError> {
        let length = candidate.chars().count();
        if length != HEX_LENGTH {
            return Err(CertificateFingerprintError::WrongLength { length });
        }

        if !candidate
            .chars()
            .all(|character| matches!(character, '0'..='9' | 'a'..='f'))
        {
            return Err(CertificateFingerprintError::NotLowercaseHex);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The fingerprint as 64 lowercase hex characters.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

impl PartialEq for CertificateFingerprint {
    /// Compares in constant time. Both sides are always 64 bytes, so the length never differs
    /// and the comparison reads every byte whatever the first differing one was.
    fn eq(&self, other: &Self) -> bool {
        self.0.as_bytes().ct_eq(other.0.as_bytes()).into()
    }
}

impl Eq for CertificateFingerprint {}

#[cfg(test)]
#[path = "../../tests/validation/fleet/certificate_fingerprint_tests.rs"]
mod tests;
