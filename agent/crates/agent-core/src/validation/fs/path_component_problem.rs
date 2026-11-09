//! Why one component of a path was refused.

/// Rejection reasons for [`super::path_component::check_path_component`].
///
/// The vocabulary the two path types share. Each of them maps it onto its own
/// error enum, so a caller still reads one exhaustive list per type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum PathComponentProblem {
    /// The component was empty: a doubled or trailing separator.
    Empty,
    /// The component was `.` or `..`.
    Traversal,
    /// The component held a NUL or another control character.
    ForbiddenCharacter,
    /// The component was longer than a filesystem accepts.
    TooLong,
}
