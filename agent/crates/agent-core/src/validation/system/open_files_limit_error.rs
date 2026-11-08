//! Why an open-files limit was refused.

/// Reasons [`super::open_files_limit::OpenFilesLimit::parse`] refuses a
/// candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum OpenFilesLimitError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "unlimited": an absent wire field decodes
    /// to zero, and a `nofile` of `0` would leave the account's sessions
    /// unable to open a file.
    #[error("an open-files limit cannot be 0")]
    Zero,

    /// The candidate was above the largest figure accepted.
    #[error("`{value}` is above the largest accepted figure, {maximum}")]
    TooLarge {
        /// What was offered.
        value: u32,
        /// The largest figure accepted.
        maximum: u32,
    },
}
