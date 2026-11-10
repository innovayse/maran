//! Why a bandwidth limit was refused.

/// Reasons [`super::bandwidth_limit::BandwidthLimit::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum BandwidthLimitError {
    /// The candidate was `0`. "No limit" is the absence of a limit, stated by
    /// the caller as `None`, never a number the tool might read another way.
    #[error("a bandwidth limit cannot be 0")]
    Zero,
    /// The candidate was above the ceiling.
    #[error("a bandwidth limit above {maximum} KiB/s is not accepted")]
    TooHigh {
        /// The largest accepted figure.
        maximum: u32,
    },
}
