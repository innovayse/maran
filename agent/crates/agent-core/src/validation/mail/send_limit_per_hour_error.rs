//! Why a candidate send limit was refused.

/// Reasons [`super::send_limit_per_hour::SendLimitPerHour::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SendLimitPerHourError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "no limit": an absent wire field decodes to
    /// zero, so accepting it would turn a caller that forgot the figure into a
    /// ceiling of nothing.
    #[error("a send limit cannot be 0")]
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
