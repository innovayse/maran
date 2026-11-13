//! The address an outside fan-out is charged for.

use super::alias_fanout_error::AliasFanoutError;
use super::mail_address::MailAddress;
use super::mail_domain::MailDomain;

/// The address a fan-out is charged for: what Postfix looks up for a recipient.
///
/// Postfix expands an alias into one delivery per target, and a domain's catch-all
/// into one delivery per its target for every address of the domain no alias or
/// mailbox claims, so the hourly limit is charged by the SAME key Postfix reads.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub enum FanoutRecipient {
    /// An alias or forwarder, `local@domain`.
    Alias(MailAddress),
    /// A domain's catch-all, written `@domain` in the table, as Postfix keys it.
    CatchAll(MailDomain),
}

impl FanoutRecipient {
    /// Reads `local@domain` as an alias and `@domain` as the domain's catch-all.
    ///
    /// # Errors
    ///
    /// [`AliasFanoutError::Recipient`] for a refused address or domain.
    pub fn parse(candidate: &str) -> Result<Self, AliasFanoutError> {
        if let Some(domain) = candidate.strip_prefix('@') {
            return Ok(Self::CatchAll(
                MailDomain::parse(domain).map_err(AliasFanoutError::CatchAllDomain)?,
            ));
        }
        Ok(Self::Alias(MailAddress::parse(candidate)?))
    }

    /// The domain the recipient is on.
    #[must_use]
    pub fn domain(&self) -> &MailDomain {
        match self {
            Self::Alias(address) => address.domain(),
            Self::CatchAll(domain) => domain,
        }
    }

    /// The key as written into the map: `local@domain` or `@domain`.
    #[must_use]
    pub fn to_key(&self) -> String {
        match self {
            Self::Alias(address) => address.to_address(),
            Self::CatchAll(domain) => format!("@{}", domain.as_str()),
        }
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/fanout_recipient_tests.rs"]
mod tests;
