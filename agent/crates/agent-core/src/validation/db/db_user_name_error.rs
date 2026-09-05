//! Why a database user name was refused.

/// Rejection reasons for [`super::db_user_name::DbUserName::for_account`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DbUserNameError {
    /// Nothing was requested.
    #[error("database user name is empty")]
    Empty,
    /// A character outside `[a-z0-9]` was requested — the separator included, so
    /// a request cannot forge another account's prefix.
    #[error("database user name contains an unexpected character: {character:?}")]
    UnexpectedCharacter {
        /// The first offending character, so the operator log says which one.
        character: char,
    },
    /// The prefixed name exceeds MySQL's thirty-two byte user-name limit, which
    /// older servers silently truncate rather than refuse.
    #[error("database user name is {length} bytes, over MySQL's 32-byte limit")]
    TooLong {
        /// The length of the prefixed name that was refused.
        length: usize,
    },
}
