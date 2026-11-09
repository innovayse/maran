//! Why a candidate autoresponder body was refused.

/// Reasons [`super::autoresponder_body::AutoresponderBody::parse`] refuses a
/// candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AutoresponderBodyError {
    /// The candidate was empty.
    #[error("an autoresponder body cannot be empty")]
    Empty,

    /// Longer than the ceiling.
    #[error("an autoresponder body cannot exceed {maximum} bytes")]
    TooLong {
        /// The longest accepted, in bytes.
        maximum: usize,
    },

    /// A control character other than the line feed, or a character that
    /// reorders text on screen. A carriage return and a tab are included.
    #[error("an autoresponder body cannot contain a control character")]
    ControlCharacter,

    /// A line begins with a dot, which in the script language's multi-line text
    /// ends the text and starts the script again.
    #[error("no line of an autoresponder body may begin with a dot")]
    LeadingDot,

    /// The text contains `${`, which the script language reads as a variable
    /// expansion wherever that extension is switched on.
    #[error("an autoresponder body cannot contain `${{`")]
    VariableExpansion,
}
