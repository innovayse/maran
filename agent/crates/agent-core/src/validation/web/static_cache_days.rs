//! How long a browser may keep a site's static files, in days.

use super::static_cache_days_error::StaticCacheDaysError;

/// The longest lifetime a site may set, in days: one year, the most a
/// `Cache-Control` / `Expires` header is worth sending.
pub const MAXIMUM_DAYS: u32 = 365;

/// A validated cache lifetime in `1..=365` days.
///
/// The inner value is private and the only constructor is
/// [`StaticCacheDays::parse`]. It is written into the vhost as `<n>d` by the
/// template, so the directive's argument is a number this type produced and
/// never text from the caller.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct StaticCacheDays(u32);

impl StaticCacheDays {
    /// Validates `candidate` as a lifetime in days and wraps it.
    ///
    /// # Errors
    ///
    /// - [`StaticCacheDaysError::Zero`] when `candidate` is `0`.
    /// - [`StaticCacheDaysError::TooLarge`] when it is above [`MAXIMUM_DAYS`].
    pub fn parse(candidate: u32) -> Result<Self, StaticCacheDaysError> {
        if candidate == 0 {
            return Err(StaticCacheDaysError::Zero);
        }
        if candidate > MAXIMUM_DAYS {
            return Err(StaticCacheDaysError::TooLarge {
                value: candidate,
                maximum: MAXIMUM_DAYS,
            });
        }

        Ok(Self(candidate))
    }

    /// The lifetime in days.
    #[must_use]
    pub fn days(&self) -> u32 {
        self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/static_cache_days_tests.rs"]
mod tests;
