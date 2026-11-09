//! Why a candidate request-body ceiling was refused.

/// Reasons [`super::client_body_size::ClientBodySize::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ClientBodySizeError {
    /// The candidate was `0`, which is what an absent field decodes to.
    ///
    /// Refused here so "the caller sent no ceiling" cannot be mistaken for
    /// "the caller asked for a ceiling of nothing": a caller that wants the
    /// default does not construct this type at all.
    #[error("a request-body ceiling cannot be 0 MiB")]
    Zero,

    /// The candidate was above the largest ceiling a site may set.
    #[error("`{value}` MiB is above the largest request-body ceiling, {maximum} MiB")]
    TooLarge {
        /// What was offered, in MiB.
        value: u32,
        /// The largest ceiling accepted, in MiB.
        maximum: u32,
    },
}
