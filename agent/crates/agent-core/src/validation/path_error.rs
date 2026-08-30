//! Why a customer-supplied path was refused.

/// Rejection reasons for [`super::path::resolve_in_home`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum PathError {
    /// The path, or one of its parents, does not exist.
    #[error("path not found")]
    NotFound,
    /// The path leaves the account home once resolved.
    #[error("path escapes account home")]
    EscapesHome,
}
