//! Why a host key fingerprint was refused.

/// Reasons [`super::host_key_fingerprint::HostKeyFingerprint::parse`] refuses a
/// candidate.
///
/// No variant carries the refused text: a fingerprint is what an administrator
/// compares against the source's console, and an operator log line needs to know
/// WHICH rule refused it, not the bytes.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum HostKeyFingerprintError {
    /// The candidate does not start with `SHA256:`, the only digest `ssh` prints
    /// by default and the only one this agent accepts.
    #[error("a host key fingerprint must start with `SHA256:`")]
    MissingPrefix,

    /// The part after the prefix is not 43 characters, the unpadded base64 length
    /// of a 32-byte SHA-256 digest.
    #[error("a host key fingerprint must carry 43 base64 characters after `SHA256:`")]
    WrongLength,

    /// The part after the prefix is not canonical unpadded base64: a character
    /// outside the standard alphabet, a `=`, or non-zero bits after the digest.
    #[error("a host key fingerprint is not canonical unpadded base64")]
    InvalidEncoding,
}
