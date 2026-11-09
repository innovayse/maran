//! Why a candidate CAA value was refused.

/// Reasons [`super::caa_value::CaaValue::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CaaValueError {
    /// The candidate was empty.
    #[error("a CAA value cannot be empty")]
    Empty,

    /// Longer than 255 bytes.
    #[error("a CAA value cannot exceed 255 bytes")]
    TooLong,

    /// A character outside the alphabet, including a newline, a double quote and
    /// a backslash.
    #[error("a CAA value cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
