//! Why a candidate certificate fingerprint was refused.

/// Reasons [`super::certificate_fingerprint::CertificateFingerprint::parse`] refuses a candidate.
///
/// Two variants because the two refusals tell an operator different things: a fingerprint of the
/// wrong length was copied incompletely, one of the right length with other characters was copied
/// from the wrong place (a colon-separated openssl line, an upper-case rendering).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CertificateFingerprintError {
    /// The candidate was not exactly 64 characters.
    #[error("a certificate fingerprint is 64 hex characters, got {length}")]
    WrongLength {
        /// How many characters were offered.
        length: usize,
    },
    /// The candidate held a character outside `0-9a-f`.
    ///
    /// Upper case is refused too: the fingerprint is compared as text, so a second spelling of
    /// one value would be two values that disagree.
    #[error("a certificate fingerprint is lowercase hex only (0-9, a-f)")]
    NotLowercaseHex,
}
