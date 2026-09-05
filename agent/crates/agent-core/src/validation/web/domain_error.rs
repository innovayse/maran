//! Why a candidate domain was refused.

use thiserror::Error;

/// Reasons [`super::domain::Domain::parse`] refuses a candidate.
#[derive(Debug, Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DomainError {
    /// The candidate was empty.
    #[error("a domain cannot be empty")]
    Empty,

    /// Longer than the 253 characters DNS permits.
    #[error("a domain cannot exceed 253 characters")]
    TooLong,

    /// A label was empty, over 63 characters, or began or ended with a hyphen.
    #[error("`{label}` is not a valid domain label")]
    InvalidLabel {
        /// The offending label.
        label: String,
    },

    /// A character that has no place in a hostname — including, and most
    /// importantly, a newline, a carriage return or any other control
    /// character, which would end the config line this value is written into.
    #[error("a domain cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
