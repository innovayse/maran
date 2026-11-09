//! Why a candidate address was refused.

use super::mail_domain_error::MailDomainError;
use super::mail_local_part_error::MailLocalPartError;

/// Reasons [`super::mail_address::MailAddress::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailAddressError {
    /// Longer than the 254 characters RFC 5321 allows for an address.
    #[error("an address cannot exceed {maximum} characters")]
    TooLong {
        /// The longest accepted, in characters.
        maximum: usize,
    },

    /// There was no `@`.
    #[error("an address needs an `@`")]
    MissingAtSign,

    /// There was more than one `@`. The grammar has no quoted local part, so
    /// an `@` before the last one has no legitimate meaning.
    #[error("an address cannot contain more than one `@`")]
    MultipleAtSigns,

    /// The text before the `@` is not an acceptable local part.
    #[error("invalid local part: {0}")]
    LocalPart(MailLocalPartError),

    /// The text after the `@` is not an acceptable domain.
    #[error("invalid domain: {0}")]
    Domain(MailDomainError),
}
