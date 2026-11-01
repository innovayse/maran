//! Why a process ceiling was refused.

/// Reasons [`super::tasks_max::TasksMax::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum TasksMaxError {
    /// The candidate was `0`.
    ///
    /// Refused rather than read as "unlimited": an absent wire field decodes to
    /// zero, so accepting it would turn a caller that forgot the figure into a
    /// slice with no ceiling at all.
    #[error("a process ceiling cannot be 0")]
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
