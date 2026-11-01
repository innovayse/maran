//! Why a write was refused at the free-space floor.

/// The agent's refusal to start a write, decided before anything was written.
///
/// Both variants mean **nothing happened**: the operation stopped at its first
/// step. Each area wraps this in its own error enum and maps it to the wire;
/// [`FloorRefusal::BelowFloor`] is `ERROR_CODE_BELOW_FREE_SPACE_FLOOR` and
/// [`FloorRefusal::Unmeasurable`] is a system failure.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum FloorRefusal {
    /// The filesystem the write lands on has less than the floor available
    /// to an unprivileged writer.
    #[error("{available} bytes available, below the free-space floor of {floor} bytes")]
    BelowFloor {
        /// Bytes an unprivileged writer could still add (`f_bavail`).
        available: u64,
        /// The configured floor, in bytes.
        floor: u64,
    },
    /// The filesystem could not be measured, so the write was not admitted.
    #[error("the filesystem the write lands on could not be measured")]
    Unmeasurable,
}
