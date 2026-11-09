//! Why a caller-supplied mutable path was refused.

use super::browse_path_error::BrowsePathError;

/// Rejection reasons for [`super::mutable_path::MutablePath::parse`].
///
/// Text here is for the operator log. The variants carry no fragment of the
/// path that produced them (rules/rust.md "Errors").
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MutablePathError {
    /// The text is not a well-formed path inside a home (see [`BrowsePathError`]).
    #[error("invalid path: {0}")]
    Malformed(#[from] BrowsePathError),
    /// The path was empty, which names the home itself; a mutation always
    /// addresses an entry INSIDE the home.
    #[error("path names the home itself")]
    Home,
    /// The first component is the file manager's reserved trash directory.
    #[error("path names the reserved trash directory")]
    Reserved,
}
