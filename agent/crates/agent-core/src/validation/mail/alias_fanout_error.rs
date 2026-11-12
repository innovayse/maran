//! Why an alias fan-out was refused.

use super::mail_address_error::MailAddressError;
use super::mail_domain_error::MailDomainError;

/// Reasons [`super::alias_fanout::AliasFanout::parse`] refuses a fan-out.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum AliasFanoutError {
    /// The alias is not an acceptable address.
    #[error("alias: {0}")]
    Recipient(#[from] MailAddressError),

    /// The domain of a catch-all (`@domain`) is not an acceptable domain.
    #[error("catch-all domain: {0}")]
    CatchAllDomain(MailDomainError),

    /// The count of outside deliveries is outside what Postfix makes for one
    /// recipient.
    ///
    /// Zero is not refused: it is an alias that stays inside the account.
    #[error("the number of outside targets must be at most {maximum}")]
    ExternalTargetsOutOfRange {
        /// The most deliveries Postfix makes for one recipient.
        maximum: usize,
    },
}
