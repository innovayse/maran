//! How many rotations of a site's log are kept.

use super::log_retention_error::LogRetentionError;

/// The largest retention accepted: the `rotate 14` of the installer's shared
/// `maran-sites` logrotate policy, which stays the hard bound. A site's own
/// setting can only shorten what that policy keeps, never lengthen it.
pub const MAXIMUM: u32 = 14;

/// How many rotated generations of a site's logs are kept: `1..=14`.
///
/// Generation `N` is the file `<domain>.<kind>.log.N[.gz]`; keeping `3` keeps
/// `.1`, `.2` and `.3` and removes everything numbered above.
///
/// The inner value is private and [`LogRetention::parse`] is the only
/// constructor, so holding one is proof the figure is in `1..={MAXIMUM}`. Zero
/// is refused rather than read as "keep nothing": an absent wire field decodes
/// to zero.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct LogRetention(u32);

impl LogRetention {
    /// Validates `candidate` as a retention and wraps it.
    ///
    /// # Errors
    ///
    /// - [`LogRetentionError::Zero`] when `candidate` is `0`.
    /// - [`LogRetentionError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, LogRetentionError> {
        if candidate == 0 {
            return Err(LogRetentionError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(LogRetentionError::TooLarge {
                value: candidate,
                maximum: MAXIMUM,
            });
        }

        Ok(Self(candidate))
    }

    /// The validated figure.
    #[must_use]
    pub fn value(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/log_retention_tests.rs"]
mod tests;
