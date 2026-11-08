//! The local part of an address this host hosts: a mailbox or an alias.

use super::check_local_part::check_local_part;
use super::mail_local_part_error::MailLocalPartError;

/// A validated local part of a hosted address, lower-cased.
///
/// It names a mailbox directory under a customer's home and a line in the
/// login database and the mail maps; `check_local_part` states why the
/// grammar is what it is. A `+` is NOT in it: the MTA splits `name+tag` at the
/// plus and delivers to `name`, so a mailbox called `a+b` could never receive.
///
/// Construction is the only way to obtain one, so a `MailLocalPart` in a
/// signature is a promise that the value went through [`MailLocalPart::parse`].
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct MailLocalPart(String);

impl MailLocalPart {
    /// Validates `candidate` and wraps it, lower-cased.
    ///
    /// # Errors
    ///
    /// Every error of the shared local-part grammar: [`MailLocalPartError::Empty`],
    /// [`MailLocalPartError::TooLong`], [`MailLocalPartError::IllegalCharacter`]
    /// (the plus sign included), [`MailLocalPartError::EdgeNotAlphanumeric`] and
    /// [`MailLocalPartError::ConsecutiveDots`].
    pub fn parse(candidate: &str) -> Result<Self, MailLocalPartError> {
        check_local_part(candidate, false).map(Self)
    }

    /// The validated local part.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_local_part_tests.rs"]
mod tests;
