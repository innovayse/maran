//! Why a candidate cache lifetime was refused.

/// Reasons [`super::static_cache_days::StaticCacheDays::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum StaticCacheDaysError {
    /// The candidate was `0`, which is what an absent field decodes to.
    ///
    /// "No cache rule" is the absence of this type, not a value of it.
    #[error("a cache lifetime cannot be 0 days")]
    Zero,

    /// The candidate was above the longest lifetime a site may set.
    #[error("`{value}` days is above the longest cache lifetime, {maximum} days")]
    TooLarge {
        /// What was offered, in days.
        value: u32,
        /// The longest lifetime accepted, in days.
        maximum: u32,
    },
}
