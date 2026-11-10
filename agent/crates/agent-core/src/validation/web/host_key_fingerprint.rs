//! The `SHA256:<base64>` fingerprint `ssh` prints for a host key.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD_NO_PAD;

use super::host_key_fingerprint_error::HostKeyFingerprintError;

/// The text every fingerprint starts with.
const PREFIX: &str = "SHA256:";

/// How many bytes a SHA-256 digest holds.
pub const HOST_KEY_DIGEST_BYTES: usize = 32;

/// How many characters the digest takes in unpadded base64: 32 bytes are 256
/// bits, which is 43 six-bit groups.
const ENCODED_LENGTH: usize = 43;

/// The fingerprint of a source's host key, in the form `ssh-keygen -l` and
/// `ssh-keyscan` output prints and an administrator reads from the source's own
/// console: `SHA256:` and 43 base64 characters, no padding.
///
/// It is the value the administrator confirms before a key is pinned, so there
/// is one spelling of it: the standard alphabet (`+` and `/`, not the URL one),
/// no `=`, and no stray bits after the 256 digest bits. A candidate that decodes
/// to the same digest but is spelled otherwise is refused rather than
/// normalised, because the backend compares the typed text against the stored
/// text byte for byte and a second spelling would make that comparison lie.
///
/// Constructed by [`HostKeyFingerprint::parse`] for text, or by
/// [`HostKeyFingerprint::from_digest`] for the SHA-256 digest of a key the agent
/// hashed itself; both give the same canonical text.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct HostKeyFingerprint(String);

impl HostKeyFingerprint {
    /// Parses the `SHA256:<base64>` text.
    ///
    /// # Errors
    ///
    /// - [`HostKeyFingerprintError::MissingPrefix`] when `candidate` does not
    ///   start with `SHA256:` (case-sensitive, as `ssh` prints it).
    /// - [`HostKeyFingerprintError::WrongLength`] when the rest is not 43
    ///   characters.
    /// - [`HostKeyFingerprintError::InvalidEncoding`] when the rest is not
    ///   canonical unpadded base64 of 32 bytes.
    pub fn parse(candidate: &str) -> Result<Self, HostKeyFingerprintError> {
        let encoded = candidate
            .strip_prefix(PREFIX)
            .ok_or(HostKeyFingerprintError::MissingPrefix)?;
        if encoded.len() != ENCODED_LENGTH {
            return Err(HostKeyFingerprintError::WrongLength);
        }

        // The engine's default is the strict one: it refuses non-zero trailing
        // bits and any padding, so the digest has exactly one spelling.
        let digest = STANDARD_NO_PAD
            .decode(encoded)
            .map_err(|_| HostKeyFingerprintError::InvalidEncoding)?;
        if digest.len() != HOST_KEY_DIGEST_BYTES {
            return Err(HostKeyFingerprintError::InvalidEncoding);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The fingerprint of the key whose SHA-256 digest is `digest`.
    ///
    /// Infallible: any 32 bytes have a fingerprint. This is how the agent names
    /// a key it hashed itself, so it never has to format the text by hand.
    #[must_use]
    pub fn from_digest(digest: &[u8; HOST_KEY_DIGEST_BYTES]) -> Self {
        Self(format!("{PREFIX}{}", STANDARD_NO_PAD.encode(digest)))
    }

    /// The canonical text, `SHA256:` and the unpadded base64 digest.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/host_key_fingerprint_tests.rs"]
mod tests;
