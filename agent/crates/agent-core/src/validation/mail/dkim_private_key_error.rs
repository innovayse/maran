//! Why a candidate DKIM private key was refused.

/// Reasons [`super::dkim_private_key::DkimPrivateKey::parse`] refuses a
/// candidate.
///
/// No variant carries the candidate or any part of it: a refused key is still
/// a private key, and an error that is logged must not echo it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DkimPrivateKeyError {
    /// Longer than any key the panel generates.
    #[error("a DKIM private key cannot exceed {maximum} bytes")]
    TooLong {
        /// The longest accepted, in bytes.
        maximum: usize,
    },

    /// A control character other than the line feed.
    #[error("a DKIM private key cannot contain a control character")]
    ControlCharacter,

    /// Not a PKCS#8 PEM document (`BEGIN PRIVATE KEY` … `END PRIVATE KEY`) of
    /// regular lines.
    #[error("a DKIM private key must be a PKCS#8 PEM document")]
    NotPkcs8Pem,

    /// The body between the markers is not canonical base64.
    #[error("the DKIM private key body is not valid base64")]
    InvalidEncoding,

    /// The document is a PKCS#8 key, but not of the RSA algorithm.
    #[error("a DKIM private key must be an RSA key")]
    NotAnRsaKey,

    /// The decoded document is not the structure of a PKCS#8 RSA key.
    #[error("the DKIM private key is malformed")]
    Malformed,

    /// The RSA modulus is outside the accepted size.
    #[error("an RSA DKIM key must be between {minimum} and {maximum} bits, this one is {bits}")]
    KeySizeRefused {
        /// The size offered, in bits.
        bits: usize,
        /// The smallest accepted, in bits.
        minimum: usize,
        /// The largest accepted, in bits.
        maximum: usize,
    },
}
