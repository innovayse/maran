//! The most mail, in MiB, one mailbox may store.

use super::mailbox_quota_mib_error::MailboxQuotaMibError;

/// The largest figure accepted, one tebibyte in MiB. It exists so an extra digit is refused
/// here instead of reaching a daemon's configuration as a nonsense figure.
pub const MAXIMUM: u32 = 1_048_576;

/// The most mail, in MiB, one mailbox may store.
///
/// The inner value is private and [`MailboxQuotaMib::parse`] is the only constructor,
/// so holding one is proof the figure is in `1..={MAXIMUM}`. Zero is refused
/// rather than read as "no limit": an absent wire field decodes to zero, and
/// "no limit" would be a separate, typed answer the caller states on purpose.
///
/// Written into configuration as a decimal integer only, so the text can
/// carry nothing but digits (rules/security.md item 4).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct MailboxQuotaMib(u32);

impl MailboxQuotaMib {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`MailboxQuotaMibError::Zero`] when `candidate` is `0`.
    /// - [`MailboxQuotaMibError::TooLarge`] when `candidate` is above [`MAXIMUM`].
    pub fn parse(candidate: u32) -> Result<Self, MailboxQuotaMibError> {
        if candidate == 0 {
            return Err(MailboxQuotaMibError::Zero);
        }

        if candidate > MAXIMUM {
            return Err(MailboxQuotaMibError::TooLarge {
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
#[path = "../../tests/validation/mail/mailbox_quota_mib_tests.rs"]
mod tests;
