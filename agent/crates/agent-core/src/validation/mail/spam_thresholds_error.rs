//! Why a set of spam thresholds was refused.

/// Reasons [`super::spam_thresholds::SpamThresholds::parse`] refuses a triple.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SpamThresholdsError {
    /// A score was `0`. Refused rather than read as "never act": an absent
    /// wire field decodes to zero.
    #[error("a spam score cannot be 0")]
    Zero,

    /// A score was above the ceiling.
    #[error("a spam score cannot exceed {maximum}")]
    TooLarge {
        /// The highest score accepted.
        maximum: u32,
    },

    /// The scores were not strictly increasing from greylist to add-header to
    /// reject.
    #[error(
        "the greylist score must be below the add-header score, which must be below the reject score"
    )]
    NotAscending,
}
