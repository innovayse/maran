//! Why a log retention was refused.

/// Reasons [`super::log_retention::LogRetention::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum LogRetentionError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "keep nothing": an absent wire field decodes
    /// to zero, so accepting it would turn a caller that forgot the figure into
    /// a deletion of every rotated archive.
    #[error("a log retention cannot be 0")]
    Zero,

    /// The candidate was above the most rotations the installer's shared policy keeps.
    #[error("`{value}` is above the largest accepted retention, {maximum}")]
    TooLarge {
        /// What was offered.
        value: u32,
        /// The largest figure accepted.
        maximum: u32,
    },
}
