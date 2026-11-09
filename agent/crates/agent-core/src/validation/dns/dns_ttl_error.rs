//! Why a candidate TTL was refused.

/// Reasons [`super::dns_ttl::DnsTtl::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum DnsTtlError {
    /// Below 60 seconds, zero (an absent field) included.
    #[error("`{value}` is below the least TTL, 60 seconds")]
    TooSmall {
        /// What was offered.
        value: u32,
    },

    /// Above 604800 seconds.
    #[error("`{value}` is above the greatest TTL, 604800 seconds")]
    TooLarge {
        /// What was offered.
        value: u32,
    },
}
