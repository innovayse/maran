//! The least room a filesystem must still have before the agent writes into
//! it (issue #148).

use std::io::Error;

use crate::utils::floor_refusal::FloorRefusal;
use crate::utils::free_space_floor_error::FreeSpaceFloorError;

/// Bytes in one mebibyte, the unit the floor is configured in.
const BYTES_PER_MEBIBYTE: u64 = 1024 * 1024;

/// How much room an unprivileged writer must still have on a filesystem before
/// the agent starts an operation that writes into it.
///
/// Written forward from the question: *a write that runs out of room half way
/// leaves a half-made account, backup or upload, so how much room must be left
/// before one is begun?* The answer is a number the operator chooses (the
/// agent's `--free-space-floor-mib`, written by the installer), compared with
/// `f_bavail` — what any non-root writer may still add, which is the figure
/// that reaches zero first and the one every hosting account fails on.
///
/// It is a pre-flight refusal and not a reservation: another writer may fill
/// the disk after the check passes. The write paths behind it keep their own
/// all-or-nothing discipline for that case; the floor makes it rare, it is not
/// what makes it safe.
///
/// A floor of zero cannot be built. A configuration that says "no floor" is
/// refused at start-up rather than read as one, because a check that admits
/// everything looks exactly like a check that works.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FreeSpaceFloor {
    /// The floor, in bytes. Always at least one mebibyte.
    bytes: u64,
}

impl FreeSpaceFloor {
    /// The floor in mebibytes when the command line names none: one gibibyte.
    ///
    /// Large enough that PostgreSQL, the agent's socket and the logs can still
    /// write while an operator frees space, and small enough not to refuse on
    /// the smallest disk this product installs on.
    pub const DEFAULT_MEBIBYTES: u64 = 1024;

    /// The floor of [`Self::DEFAULT_MEBIBYTES`].
    #[must_use]
    pub const fn default_floor() -> Self {
        Self {
            bytes: Self::DEFAULT_MEBIBYTES * BYTES_PER_MEBIBYTE,
        }
    }

    /// Builds a floor of `mebibytes` mebibytes.
    ///
    /// # Errors
    ///
    /// - [`FreeSpaceFloorError::Zero`] for zero, which would admit every write.
    /// - [`FreeSpaceFloorError::TooLarge`] when the byte count does not fit in
    ///   a `u64`.
    pub fn from_mebibytes(mebibytes: u64) -> Result<Self, FreeSpaceFloorError> {
        if mebibytes == 0 {
            return Err(FreeSpaceFloorError::Zero);
        }

        let bytes = mebibytes
            .checked_mul(BYTES_PER_MEBIBYTE)
            .ok_or(FreeSpaceFloorError::TooLarge { mebibytes })?;

        Ok(Self { bytes })
    }

    /// The floor, in bytes.
    #[must_use]
    pub const fn bytes(&self) -> u64 {
        self.bytes
    }

    /// Admits a write onto a filesystem whose available space was `measured`.
    ///
    /// Takes the measurement rather than a path so the decision is a pure
    /// function a test can drive with any figure: each area asks its own host
    /// seam for the bytes available under the directory the write lands in
    /// ([`available_bytes`](crate::utils::available_bytes::available_bytes) on
    /// a real host) and hands the answer here unopened.
    ///
    /// Exactly the floor left is admitted; one byte less is refused.
    ///
    /// # Errors
    ///
    /// - [`FloorRefusal::Unmeasurable`] when the measurement failed. Never a
    ///   generous default: an unknown filesystem is not a roomy one.
    /// - [`FloorRefusal::BelowFloor`] when less than the floor is available.
    pub fn admit(&self, measured: Result<u64, Error>) -> Result<(), FloorRefusal> {
        let available = measured.map_err(|_error| FloorRefusal::Unmeasurable)?;

        if available < self.bytes {
            return Err(FloorRefusal::BelowFloor {
                available,
                floor: self.bytes,
            });
        }

        Ok(())
    }
}

#[cfg(test)]
#[path = "../tests/utils/free_space_floor_tests.rs"]
mod tests;
