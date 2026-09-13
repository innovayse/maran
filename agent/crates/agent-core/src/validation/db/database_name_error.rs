//! Why a database name was refused.

/// Rejection reasons for [`super::database_name::DatabaseName::for_account`].
///
/// Unlike [`NameError`](crate::validation::system::name_error::NameError), which is deliberately opaque because
/// it answers an authentication-adjacent boundary, these variants name the fault:
/// the caller is the panel, which asked its own customer for the name and has to
/// tell them what to change.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DatabaseNameError {
    /// Nothing was requested.
    #[error("database name is empty")]
    Empty,
    /// A character outside `[a-z0-9]` was requested — the separator included, so
    /// a request cannot forge another account's prefix.
    #[error("database name contains an unexpected character: {character:?}")]
    UnexpectedCharacter {
        /// The first offending character, so the operator log says which one.
        character: char,
    },
    /// The prefixed name exceeds MySQL's sixty-four byte identifier limit.
    #[error("database name is {length} bytes, over MySQL's 64-byte limit")]
    TooLong {
        /// The length of the prefixed name that was refused.
        length: usize,
    },
}
