//! The name the mail server presents to the world.

use super::mail_domain::MailDomain;
use super::mail_server_hostname_error::MailServerHostnameError;

/// The fully qualified name the MTA and the IMAP server present (the SMTP
/// greeting and the TLS certificate name), lower-cased.
///
/// Its grammar is a mail domain's: a name with a dot, not reserved, well
/// formed. It is a type of its own because it MEANS something else — it names
/// this machine, not a customer's domain — and a value accepted as one must not
/// be accepted as the other by accident of both being a `MailDomain`.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct MailServerHostname(MailDomain);

impl MailServerHostname {
    /// Validates `candidate` and wraps it, lower-cased.
    ///
    /// # Errors
    ///
    /// [`MailServerHostnameError::Invalid`] carrying whatever
    /// [`MailDomain::parse`] refused.
    pub fn parse(candidate: &str) -> Result<Self, MailServerHostnameError> {
        Ok(Self(MailDomain::parse(candidate)?))
    }

    /// The validated hostname.
    #[must_use]
    pub fn as_str(&self) -> &str {
        self.0.as_str()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_server_hostname_tests.rs"]
mod tests;
