//! A domain the host accepts and sends mail for.

use super::mail_domain_error::MailDomainError;
use crate::validation::web::domain::Domain;

/// The longest mail domain, in characters: an address is at most 254
/// characters (RFC 5321 forward-path), a local part takes up to 64 and the `@`
/// one, so the domain gets what is left. A longer one could be hosted and then
/// never addressed.
pub const MAXIMUM_LENGTH: usize = 189;

/// A validated mail domain, lower-cased.
///
/// It is a [`Domain`] a tenant may claim (well-formed, not reserved) with two
/// further rules that only mail has: at least two labels, and short enough
/// that every address under it fits. It is written into the MTA's domain map,
/// into a path under the customer's home and into the DKIM signing map; the
/// [`Domain`] alphabet contains no separator any of them uses.
///
/// Whether the account is ALLOWED this domain (it must be a hostname of one of
/// its own sites) is the panel's decision and not this type's: a spelling check
/// cannot know who owns what.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct MailDomain(String);

impl MailDomain {
    /// Validates `candidate` and wraps it, lower-cased.
    ///
    /// # Errors
    ///
    /// - [`MailDomainError::Invalid`] for everything [`Domain::parse`] refuses.
    /// - [`MailDomainError::SingleLabel`] when there is no dot.
    /// - [`MailDomainError::TooLong`] beyond [`MAXIMUM_LENGTH`] characters.
    pub fn parse(candidate: &str) -> Result<Self, MailDomainError> {
        let domain = Domain::parse(candidate)?;

        if !domain.as_str().contains('.') {
            return Err(MailDomainError::SingleLabel);
        }

        if domain.as_str().len() > MAXIMUM_LENGTH {
            return Err(MailDomainError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        Ok(Self(domain.as_str().to_owned()))
    }

    /// The validated domain.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_domain_tests.rs"]
mod tests;
