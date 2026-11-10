//! Why a remote database name was refused.

/// Reasons [`super::remote_database_name::RemoteDatabaseName::parse`] refuses a
/// candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RemoteDatabaseNameError {
    /// Nothing was given.
    #[error("a database name cannot be empty")]
    Empty,
    /// The candidate was longer than MySQL's identifier ceiling.
    #[error("a database name is at most {maximum} characters")]
    TooLong {
        /// The most characters accepted.
        maximum: usize,
    },
    /// The candidate held a character outside `[A-Za-z0-9_]`.
    #[error("a database name holds a character that is not a letter, a digit or an underscore")]
    UnexpectedCharacter,
}
