//! How fast a migration transfer may read from its source.

use super::bandwidth_limit_error::BandwidthLimitError;

/// The largest accepted limit, in kibibytes per second: 1 GiB/s, far above any
/// link a source is reached over, and small enough that no arithmetic on it can
/// overflow.
const MAXIMUM_KIBIBYTES_PER_SECOND: u32 = 1_048_576;

/// A bandwidth limit for one migration transfer, in kibibytes per second.
///
/// It becomes `rsync`'s `--bwlimit=<n>`, whose unit is KiB/s. The inner figure
/// is private and [`BandwidthLimit::parse`] is the only constructor, so holding
/// one is proof the figure is in `1..=1_048_576`. Zero is refused rather than
/// read as "no limit": `rsync` reads `--bwlimit=0` as unlimited, and an absent
/// wire field decodes to `0`, so accepting it would turn a caller that forgot
/// the figure into a transfer with no ceiling. "No limit" is `None` where the
/// caller holds an `Option<BandwidthLimit>`, stated on purpose.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct BandwidthLimit(u32);

impl BandwidthLimit {
    /// Validates `candidate` as a limit in KiB/s and wraps it.
    ///
    /// # Errors
    ///
    /// - [`BandwidthLimitError::Zero`] when `candidate` is `0`.
    /// - [`BandwidthLimitError::TooHigh`] when it is above 1 GiB/s.
    pub fn parse(candidate: u32) -> Result<Self, BandwidthLimitError> {
        if candidate == 0 {
            return Err(BandwidthLimitError::Zero);
        }
        if candidate > MAXIMUM_KIBIBYTES_PER_SECOND {
            return Err(BandwidthLimitError::TooHigh {
                maximum: MAXIMUM_KIBIBYTES_PER_SECOND,
            });
        }

        Ok(Self(candidate))
    }

    /// The validated figure, in KiB/s.
    #[must_use]
    pub fn kibibytes_per_second(&self) -> u32 {
        self.0
    }

    /// The `rsync` option that applies the limit.
    ///
    /// Built from a validated integer, so the text is `--bwlimit=` and digits:
    /// no newline, no second option (rules/security.md item 4).
    #[must_use]
    pub fn as_argument(&self) -> String {
        format!("--bwlimit={}", self.0)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/bandwidth_limit_tests.rs"]
mod tests;
