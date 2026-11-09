//! Why a caller-supplied browse path was refused.

/// Rejection reasons for [`super::browse_path::BrowsePath::parse`].
///
/// Text here is for the operator log. The variants carry no fragment of the
/// path that produced them (rules/rust.md "Errors").
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BrowsePathError {
    /// The path began with `/`, so it names a location unrelated to the home.
    #[error("path is absolute")]
    Absolute,
    /// Two separators in a row, or a trailing separator.
    #[error("path has an empty component")]
    EmptyComponent,
    /// A component was `.` or `..`; refused rather than normalised away.
    #[error("path component traverses the tree")]
    Traversal,
    /// A component held a NUL or another control character.
    #[error("path component holds a control character")]
    ForbiddenCharacter,
    /// A component was longer than a filesystem accepts.
    #[error("path component is too long")]
    ComponentTooLong,
    /// The path had more components than the agent will descend.
    #[error("path has too many components")]
    TooDeep,
    /// The whole path was longer than the kernel's path limit.
    #[error("path is too long")]
    TooLong,
}
