//! Why a candidate CAA flags value was refused.

/// Reasons [`super::caa_flags::CaaFlags::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum CaaFlagsError {
    /// Neither `0` nor `128`.
    #[error("`{value}` is not a CAA flags value; use 0 or 128")]
    Unknown {
        /// What was offered.
        value: u32,
    },
}
