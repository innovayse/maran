//! Why a candidate mail domain was refused.

use crate::validation::web::domain_error::DomainError;

/// Reasons [`super::mail_domain::MailDomain::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailDomainError {
    /// The candidate is not a name a tenant may claim at all: malformed, or in
    /// the reserved set. The wrapped error says which.
    #[error("not a valid domain: {0}")]
    Invalid(#[from] DomainError),

    /// A single label such as `shop`. An address needs a domain with a dot, so
    /// a single label is a name no other host can send mail to.
    #[error("a mail domain needs at least two labels")]
    SingleLabel,

    /// Longer than the longest domain that still fits an address of at most
    /// 254 characters beside a 64-character local part.
    #[error("a mail domain cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },
}
