//! Why a host key was refused.

/// Reasons [`super::host_key::HostKey::parse`] refuses a candidate.
///
/// No variant carries the refused text: it is the pin a connection is trusted
/// against, and an operator log line needs to know WHICH rule refused it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum HostKeyError {
    /// The candidate was empty.
    #[error("a host key cannot be empty")]
    Empty,

    /// The candidate is longer than the ceiling that bounds the base64 field.
    #[error("a host key is longer than the accepted ceiling")]
    TooLong,

    /// A control character, a newline above all: one would turn the single
    /// `known_hosts` line this key becomes into several.
    #[error("a host key cannot contain a control character")]
    ControlCharacter,

    /// The candidate is not exactly `<algorithm> <base64>`: a field is missing,
    /// or more than one separator, a comment or an option is present.
    #[error("a host key must be exactly `<algorithm> <base64>`")]
    NotTwoFields,

    /// The algorithm is not on the allow-list.
    #[error("a host key algorithm is not accepted")]
    UnsupportedAlgorithm,

    /// The second field is empty or is not standard base64.
    #[error("a host key is not valid base64")]
    InvalidEncoding,

    /// The decoded key is too short to carry an algorithm name.
    #[error("a host key is malformed")]
    Malformed,

    /// The decoded key names a different algorithm from the first field.
    #[error("a host key names a different algorithm than it declares")]
    AlgorithmMismatch,
}
