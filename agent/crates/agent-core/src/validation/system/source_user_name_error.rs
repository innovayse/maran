//! Why a remote login name was refused.

/// Reasons [`super::source_user_name::SourceUserName::parse`] refuses a candidate.
///
/// No variant carries the refused text; the operator log needs to know WHICH
/// rule refused it.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SourceUserNameError {
    /// The candidate was empty.
    #[error("a remote login name cannot be empty")]
    Empty,

    /// The candidate is longer than the 32 characters a Linux login may have.
    #[error("a remote login name cannot exceed {limit} characters")]
    TooLong {
        /// The ceiling it was measured against.
        limit: usize,
    },

    /// The first character is not a lowercase letter or an underscore. A leading
    /// `-` in particular would be read by `ssh` as an option.
    #[error("a remote login name must start with a lowercase letter or an underscore")]
    BadFirstCharacter,

    /// A character outside lowercase letters, digits, `_` and `-`.
    #[error("a remote login name contains a character outside [a-z0-9_-]")]
    IllegalCharacter,
}
