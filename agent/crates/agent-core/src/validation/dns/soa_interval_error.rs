//! Why a candidate SOA interval was refused.

/// Reasons [`super::soa_interval::SoaInterval::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SoaIntervalError {
    /// Below 60 seconds, zero (an absent field) included.
    #[error("`{value}` is below the least interval, 60 seconds")]
    TooSmall {
        /// What was offered.
        value: u32,
    },

    /// Above 2419200 seconds.
    #[error("`{value}` is above the greatest interval, 2419200 seconds")]
    TooLarge {
        /// What was offered.
        value: u32,
    },
}
