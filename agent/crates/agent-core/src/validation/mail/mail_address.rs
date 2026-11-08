//! A full address, as a target of an alias or a forwarder.

use super::check_local_part::check_local_part;
use super::mail_address_error::MailAddressError;
use super::mail_domain::MailDomain;

/// The longest address, in characters, from RFC 5321's forward-path.
pub const MAXIMUM_LENGTH: usize = 254;

/// A validated `local@domain`, both halves lower-cased.
///
/// It is where mail is SENT TO — an alias's target, a forwarder's destination —
/// so it may name this host or another, and its local part is read with the
/// extension delimiter allowed (`name+tag@example.org` is a common way to
/// forward). The domain half is a [`MailDomain`], so a target on `localhost`
/// or an address literal such as `user@[127.0.0.1]` is refused rather than
/// handed to the MTA as a way to reach the host's own services.
///
/// There is no quoted form: `"a b"@example.org` is legal mail and not
/// accepted here, because the value is written into a whitespace-separated
/// map and quoting is exactly the thing that must not be interpreted.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct MailAddress {
    /// The local part, lower-cased, extension delimiter allowed.
    local_part: String,
    /// The domain.
    domain: MailDomain,
}

impl MailAddress {
    /// Validates `candidate` and wraps it, lower-cased.
    ///
    /// # Errors
    ///
    /// - [`MailAddressError::TooLong`] beyond [`MAXIMUM_LENGTH`] characters.
    /// - [`MailAddressError::MissingAtSign`] with no `@`.
    /// - [`MailAddressError::MultipleAtSigns`] with more than one.
    /// - [`MailAddressError::LocalPart`] for a refused local part.
    /// - [`MailAddressError::Domain`] for a refused domain.
    pub fn parse(candidate: &str) -> Result<Self, MailAddressError> {
        if candidate.chars().count() > MAXIMUM_LENGTH {
            return Err(MailAddressError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        let Some((local, domain)) = candidate.split_once('@') else {
            return Err(MailAddressError::MissingAtSign);
        };

        if domain.contains('@') {
            return Err(MailAddressError::MultipleAtSigns);
        }

        let local_part = check_local_part(local, true).map_err(MailAddressError::LocalPart)?;
        let domain = MailDomain::parse(domain).map_err(MailAddressError::Domain)?;

        Ok(Self { local_part, domain })
    }

    /// The local part, lower-cased.
    #[must_use]
    pub fn local_part(&self) -> &str {
        &self.local_part
    }

    /// The domain half.
    #[must_use]
    pub fn domain(&self) -> &MailDomain {
        &self.domain
    }

    /// The whole address, `local@domain`.
    #[must_use]
    pub fn to_address(&self) -> String {
        format!("{}@{}", self.local_part, self.domain.as_str())
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/mail_address_tests.rs"]
mod tests;
