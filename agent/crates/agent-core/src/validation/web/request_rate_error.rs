//! Why a candidate request limit was refused.

/// Reasons [`super::request_rate::RequestRate::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RequestRateError {
    /// The rate was `0`, which is what an absent field decodes to.
    ///
    /// Refused so "the caller sent no limit" cannot be mistaken for "the caller
    /// asked for a limit of nothing": a caller that wants no limit does not
    /// construct this type at all.
    #[error("a request rate cannot be 0 requests per second")]
    Zero,

    /// The rate was above the highest a site may set.
    #[error("{value} requests per second is above the highest request rate, {maximum}")]
    RateTooHigh {
        /// What was offered, in requests per second.
        value: u32,
        /// The highest rate accepted.
        maximum: u32,
    },

    /// The burst was above the largest a site may allow.
    #[error("a burst of {value} requests is above the largest burst, {maximum}")]
    BurstTooHigh {
        /// What was offered, in requests.
        value: u32,
        /// The largest burst accepted.
        maximum: u32,
    },
}
