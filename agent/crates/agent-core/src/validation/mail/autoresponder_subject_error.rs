//! Why a candidate autoresponder subject was refused.

/// Reasons [`super::autoresponder_subject::AutoresponderSubject::parse`]
/// refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AutoresponderSubjectError {
    /// The candidate was empty.
    #[error("an autoresponder subject cannot be empty")]
    Empty,

    /// Longer than the ceiling.
    #[error("an autoresponder subject cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },

    /// A control character, a line break among them, or a character that
    /// reorders text on screen.
    #[error("an autoresponder subject cannot contain a control character")]
    ControlCharacter,

    /// A double quote or a backslash, the two characters that end or alter a
    /// quoted string in the script language.
    #[error("an autoresponder subject cannot contain `{character}`")]
    QuotingCharacter {
        /// The offending character.
        character: char,
    },

    /// The text contains `${`, which the script language reads as a variable
    /// expansion wherever that extension is switched on.
    #[error("an autoresponder subject cannot contain `${{`")]
    VariableExpansion,
}
