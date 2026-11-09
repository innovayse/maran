//! Why a candidate DKIM selector was refused.

/// Reasons [`super::dkim_selector::DkimSelector::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DkimSelectorError {
    /// The candidate was empty.
    #[error("a DKIM selector cannot be empty")]
    Empty,

    /// Longer than the ceiling.
    #[error("a DKIM selector cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },

    /// A character outside lower-case letters, digits and the hyphen —
    /// including a dot, an upper-case letter, a control character and a path
    /// separator.
    #[error("a DKIM selector cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// The selector began or ended with a hyphen.
    #[error("a DKIM selector must start and end with a letter or a digit")]
    EdgeNotAlphanumeric,
}
