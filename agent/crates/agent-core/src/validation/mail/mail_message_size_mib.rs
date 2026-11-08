//! The largest message, in MiB, the MTA accepts.

use super::mail_message_size_mib_error::MailMessageSizeMibError;

/// The largest figure accepted, 512 MiB. It exists so an extra digit is refused
/// here instead of reaching a daemon's configuration as a nonsense figure.
pub const MAXIMUM: u32 = 512;

/// The largest message, in MiB, the MTA accepts.
///
/// The inner value is private and [`MailMessageSizeMib::parse`] is the only constructor,
/// so holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" would be a separate, typed answer the caller states on purpose.
///
/// Written into configuration as a decimal integer only, so the text can
/// carry nothing but digits (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct MailMessageSizeMib(u32);

impl MailMessageSizeMib {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`MailMessageSizeMibError::Zero`] when `candidate` is `0`.
    /// - [`MailMessageSizeMibError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, MailMessageSizeMibError> {
        if candidate == 0 {
            return Err(MailMessageSizeMibError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(MailMessageSizeMibError::TooLarge {
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
#[path = "../../tests/validation/mail/mail_message_size_mib_tests.rs"]
mod tests;
