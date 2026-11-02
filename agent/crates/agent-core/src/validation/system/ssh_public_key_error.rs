//! Why an SSH public key was refused.

/// Rejection reasons for [`super::ssh_public_key::SshPublicKey::parse`].
///
/// No variant carries the refused text: a key is customer input, and an
/// operator log line needs to know WHICH rule refused it, not the bytes.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SshPublicKeyError {
    /// Nothing but whitespace was supplied.
    #[error("ssh public key is empty")]
    Empty,
    /// The key is longer than the 8 KiB ceiling.
    #[error("ssh public key is {length} bytes, over the {limit}-byte ceiling")]
    TooLong {
        /// The length that was refused.
        length: usize,
        /// The ceiling it was measured against.
        limit: usize,
    },
    /// A control character — a newline, a carriage return, a NUL, a tab —
    /// appears anywhere in the key. One newline would turn one key into two
    /// lines of an `authorized_keys` file.
    #[error("ssh public key contains a control character")]
    ControlCharacter,
    /// The first field is not an accepted algorithm name. This is also the
    /// answer for a line that starts with an option such as `command="…"`:
    /// options are never parsed, so they can never be honoured.
    #[error("ssh public key algorithm is not accepted")]
    UnsupportedAlgorithm,
    /// The base64 field is missing or does not decode.
    #[error("ssh public key data is not valid base64")]
    InvalidEncoding,
    /// The decoded key is not a well-formed key of its algorithm: a field is
    /// short, a length is wrong, a curve is not the named one, or bytes trail
    /// the last field.
    #[error("ssh public key data is malformed")]
    Malformed,
    /// The decoded key names a different algorithm from the text's first field.
    #[error("ssh public key data names a different algorithm")]
    AlgorithmMismatch,
    /// An RSA modulus outside 2048 to 16384 bits.
    #[error("rsa key is {bits} bits, outside the accepted 2048 to 16384")]
    RsaSizeRefused {
        /// The modulus size that was refused.
        bits: usize,
    },
}
