//! Why a candidate domain was refused.

use thiserror::Error;

/// Reasons [`super::domain::Domain::parse`] refuses a candidate: a malformed
/// spelling, or a well-formed name that is reserved. The reserved variants carry
/// no value, so a message never says which part of the set matched.
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

    /// The last label is all digits, so the name is, or can be read as, an
    /// address literal such as `203.0.113.7` or `12345`.
    #[error("a domain cannot end in a numeric label")]
    NumericFinalLabel,

    /// The name is `localhost` or ends in `.localhost`.
    #[error("a domain cannot be a localhost name")]
    LocalhostFamily,

    /// The name is, or ends in, a special-use suffix that never names a public
    /// host (`local`, `localdomain`, `internal`, `lan`, `home.arpa`, `invalid`).
    #[error("a domain cannot use a special-use suffix")]
    SpecialUseSuffix,
}
