//! Why the name of an archive member was refused.

/// Rejection reasons for [`super::archive_member_name::ArchiveMemberName::parse`].
///
/// Every variant is a way an archive can name a place the extraction must not
/// write, or a name no filesystem could hold. Text here is for the operator
/// log: the variants carry no fragment of the name that produced them, because
/// the name came from a customer's file (rules/rust.md "Errors").
#[derive(Debug, Clone, Copy, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ArchiveMemberNameError {
    /// The name was empty.
    #[error("member name is empty")]
    Empty,
    /// The name was longer than the kernel's path limit.
    #[error("member name is too long")]
    TooLong,
    /// The name was not valid UTF-8. A byte string no later request could name
    /// back is refused rather than repaired.
    #[error("member name is not valid UTF-8")]
    NotUtf8,
    /// The name began with `/`: it names a location unrelated to the destination.
    #[error("member name is absolute")]
    Absolute,
    /// The name held a backslash, which another platform reads as a separator
    /// (`..\..\x`) and this one reads as part of a file name.
    #[error("member name holds a backslash")]
    Backslash,
    /// Two separators in a row, or a trailing separator after the one allowed.
    #[error("member name has an empty component")]
    EmptyComponent,
    /// A component was `.` or `..`.
    #[error("member name traverses the tree")]
    Traversal,
    /// A component held a NUL or another control character.
    #[error("member name holds a control character")]
    ForbiddenCharacter,
    /// A component was longer than a filesystem accepts.
    #[error("member name has a component that is too long")]
    ComponentTooLong,
    /// The name had more components than the agent will descend.
    #[error("member name has too many components")]
    TooDeep,
}
