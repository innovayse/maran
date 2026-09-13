//! Why a caller-supplied relative path was refused.

/// Rejection reasons for [`super::relative_path::RelativePath::parse`].
///
/// Text here is for the operator log. The variants carry no fragment of the
/// path that produced them: the caller already knows what it sent, and an error
/// that quotes a customer path is one more way for a path to reach a place it
/// was not reviewed for (rules/rust.md "Errors").
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RelativePathError {
    /// The path was empty, so it names nothing to write or remove.
    #[error("path is empty")]
    Empty,
    /// The path began with `/`, so it is absolute and names a location that has
    /// nothing to do with the account's home.
    #[error("path is absolute")]
    Absolute,
    /// Two separators in a row, or a trailing separator: the path names a
    /// component that is not there.
    #[error("path has an empty component")]
    EmptyComponent,
    /// A component was `.` or `..`. Refused rather than normalised away —
    /// normalising is how a path that "looks contained" is manufactured, and
    /// the panel has no reason to send either.
    #[error("path component traverses the tree")]
    Traversal,
    /// A component held a NUL or another control character. A NUL truncates the
    /// name at the C boundary, and the rest are never part of a legitimate file
    /// name (rules/security.md item 4).
    #[error("path component holds a control character")]
    ForbiddenCharacter,
    /// A component was longer than a filesystem accepts, so no write using it
    /// could succeed and refusing it early costs nothing.
    #[error("path component is too long")]
    ComponentTooLong,
    /// The path had more components than the agent will create or descend. The
    /// write creates every missing level, so without a ceiling one request
    /// could ask for arbitrarily many directories in a customer's home.
    #[error("path has too many components")]
    TooDeep,
}
