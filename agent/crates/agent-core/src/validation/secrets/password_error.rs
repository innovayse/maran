//! Why a password was refused.

/// Rejection reasons for [`super::password::Password::parse`].
///
/// No variant carries the password or any part of it: an error enum is what
/// reaches an operator log, and a rejected password is still a secret
/// (rules/security.md §8). `UnexpectedCharacter` carries the single offending
/// character, which names the rule that was broken without reproducing the
/// value.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PasswordError {
    /// The candidate is empty.
    #[error("password is empty")]
    Empty,
    /// A character outside the accepted alphabet was supplied — a quote, a
    /// backslash, a colon, a space or a control character among them.
    #[error("password contains an unexpected character: {character:?}")]
    UnexpectedCharacter {
        /// The first offending character.
        character: char,
    },
    /// The candidate is longer than the accepted maximum.
    #[error("password is {length} bytes, over the 128-byte maximum")]
    TooLong {
        /// The length of the candidate that was refused.
        length: usize,
    },
}
