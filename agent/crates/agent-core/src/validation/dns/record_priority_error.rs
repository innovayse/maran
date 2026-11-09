//! Why a candidate priority was refused.

/// Reasons [`super::record_priority::RecordPriority::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum RecordPriorityError {
    /// Above the 16-bit field.
    #[error("`{value}` is above the greatest priority, 65535")]
    TooLarge {
        /// What was offered.
        value: u32,
    },
}
