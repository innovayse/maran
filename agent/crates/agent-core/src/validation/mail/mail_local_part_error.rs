//! Why a candidate local part was refused.

/// Reasons a local part (the text before the `@`) is refused.
///
/// No variant carries more than one offending character: the candidate is
/// customer text, and an error that is logged should not echo all of it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailLocalPartError {
    /// The candidate was empty.
    #[error("a local part cannot be empty")]
    Empty,

    /// The candidate was longer than the 64 characters an address allows.
    #[error("a local part cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },

    /// A character outside the accepted alphabet — including a control
    /// character, a space, a colon, a quote and a non-ASCII letter, each of which
    /// would change what a mail map or a login database line means.
    #[error("a local part cannot contain `{character:?}`")]
    IllegalCharacter {
        /// The first offending character.
        character: char,
    },

    /// The first or the last character was not a letter or a digit.
    #[error("a local part must start and end with a letter or a digit")]
    EdgeNotAlphanumeric,

    /// Two dots in a row.
    #[error("a local part cannot contain two dots in a row")]
    ConsecutiveDots,
}
