//! Why a candidate SRV weight was refused.

/// Reasons [`super::srv_weight::SrvWeight::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum SrvWeightError {
    /// Above the 16-bit field.
    #[error("`{value}` is above the greatest weight, 65535")]
    TooLarge {
        /// What was offered.
        value: u32,
    },
}
