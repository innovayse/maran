//! The most messages one mailbox may send per hour.

use super::send_limit_per_hour_error::SendLimitPerHourError;

/// The largest figure accepted, one hundred thousand messages. It exists so an extra digit is refused
/// here instead of reaching a daemon's configuration as a nonsense figure.
pub const MAXIMUM: u32 = 100_000;

/// The most messages one mailbox may send per hour.
///
/// The inner value is private and [`SendLimitPerHour::parse`] is the only constructor,
/// so holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" would be a separate, typed answer the caller states on purpose.
///
/// Written into configuration as a decimal integer only, so the text can
/// carry nothing but digits (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct SendLimitPerHour(u32);

impl SendLimitPerHour {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`SendLimitPerHourError::Zero`] when `candidate` is `0`.
    /// - [`SendLimitPerHourError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, SendLimitPerHourError> {
        if candidate == 0 {
            return Err(SendLimitPerHourError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(SendLimitPerHourError::TooLarge {
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
#[path = "../../tests/validation/mail/send_limit_per_hour_tests.rs"]
mod tests;
