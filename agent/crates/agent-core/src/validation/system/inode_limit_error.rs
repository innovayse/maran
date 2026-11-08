//! Why an inode limit was refused.

/// Reasons [`super::inode_limit::InodeLimit::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum InodeLimitError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "unlimited": an absent wire field decodes
    /// to zero, so accepting it would turn a caller that forgot the figure
    /// into an account with no ceiling at all.
    #[error("an inode limit cannot be 0")]
    Zero,
}
