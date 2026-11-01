//! Why a configured free-space floor could not be built.

/// A free-space floor the agent refuses to run with.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum FreeSpaceFloorError {
    /// Zero mebibytes: a floor that admits every write is no floor, and is
    /// refused rather than read as "switched off".
    #[error("the free-space floor must be at least one mebibyte")]
    Zero,
    /// The byte count of `mebibytes` does not fit in 64 bits.
    #[error("a free-space floor of {mebibytes} MiB is too large")]
    TooLarge {
        /// The configured value, in mebibytes.
        mebibytes: u64,
    },
}
