//! Why a memory ceiling was refused.

/// Reasons [`super::memory_max_mib::MemoryMaxMib::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MemoryMaxMibError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "unlimited": an absent wire field decodes to
    /// zero, so accepting it would turn a caller that forgot the figure into a
    /// slice with no ceiling at all.
    #[error("a memory ceiling cannot be 0")]
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
