//! Why a candidate TXT text was refused.

/// Reasons [`super::txt_text::TxtText::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum TxtTextError {
    /// The candidate was empty.
    #[error("a TXT text cannot be empty")]
    Empty,

    /// Longer than 2048 bytes.
    #[error("a TXT text cannot exceed 2048 bytes")]
    TooLong,

    /// A character outside printable ASCII, or a double quote or backslash —
    /// including the newline that would end the zone-file line.
    #[error("a TXT text cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },
}
