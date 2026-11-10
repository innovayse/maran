//! Why a source private key was refused.

/// Rejection reasons for [`super::source_private_key::SourcePrivateKey::parse`].
///
/// No variant carries the key, a fragment of it or a position in it: an error
/// enum is what reaches an operator log, and a rejected key is still a secret
/// (rules/security.md item 8).
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SourcePrivateKeyError {
    /// The candidate was empty.
    #[error("the source private key is empty")]
    Empty,

    /// The candidate is longer than the ceiling.
    #[error("the source private key is longer than {limit} bytes")]
    TooLong {
        /// The ceiling it was measured against.
        limit: usize,
    },

    /// A byte other than printable ASCII or a line feed: a carriage return, a
    /// NUL, a tab, any other control character, or a byte of non-ASCII text.
    #[error("the source private key contains a character that has no place in one")]
    ForbiddenCharacter,

    /// The first line is not the OpenSSH private key header.
    #[error("the source private key does not start with the OpenSSH header")]
    MissingHeader,

    /// The last line is not the OpenSSH private key footer, or something follows
    /// it.
    #[error("the source private key does not end with the OpenSSH footer")]
    MissingFooter,

    /// The lines between header and footer are not one base64 document that
    /// starts with the `openssh-key-v1` magic.
    #[error("the source private key body is not an OpenSSH key document")]
    MalformedBody,
}
