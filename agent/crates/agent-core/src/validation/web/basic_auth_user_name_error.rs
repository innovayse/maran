//! Why a candidate basic-authentication user name was refused.

/// Reasons [`super::basic_auth_user_name::BasicAuthUserName::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BasicAuthUserNameError {
    /// The candidate was empty.
    #[error("a user name cannot be empty")]
    Empty,

    /// The candidate was longer than the limit.
    #[error("a user name is at most {maximum} characters, this one has {length}")]
    TooLong {
        /// The length offered, in bytes.
        length: usize,
        /// The longest accepted.
        maximum: usize,
    },

    /// The candidate held a control character (a newline included), which would
    /// end the credential line and start another.
    #[error("a user name cannot contain a control character")]
    ControlCharacter,

    /// The candidate held a character outside `[A-Za-z0-9._-]`, the colon that
    /// separates the name from the hash included.
    #[error("a user name may hold only letters, digits, `.`, `_` and `-`")]
    ForbiddenCharacter,
}
