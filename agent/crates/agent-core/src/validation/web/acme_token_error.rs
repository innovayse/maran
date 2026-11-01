//! Why an ACME challenge token was refused.

/// Reasons [`super::acme_token::AcmeToken::parse`] refuses a candidate.
///
/// The token becomes a file name written as root, so every refusal here is a
/// refusal to let a remote party choose where that file lands.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AcmeTokenError {
    /// The token was empty or longer than the ceiling.
    #[error("a challenge token is 1 to {maximum} bytes, not {actual}")]
    WrongLength {
        /// The most bytes a token may have.
        maximum: usize,
        /// The length, in bytes, that was offered.
        actual: usize,
    },

    /// A character outside the base64url alphabet was found.
    #[error("a challenge token is base64url, and `{character:?}` is not")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
