//! Why a candidate server hostname was refused.

use super::mail_domain_error::MailDomainError;

/// Reasons [`super::mail_server_hostname::MailServerHostname::parse`] refuses a
/// candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum MailServerHostnameError {
    /// The candidate is not a fully qualified name the host could present.
    #[error("not a valid server hostname: {0}")]
    Invalid(#[from] MailDomainError),
}
