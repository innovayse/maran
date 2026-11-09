//! Why a candidate zone name was refused.

use crate::validation::web::domain_error::DomainError;

/// Reasons [`super::zone_name::ZoneName::parse`] refuses a candidate.
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum ZoneNameError {
    /// The candidate was empty, or only a dot.
    #[error("a zone name cannot be empty")]
    Empty,

    /// Longer than the stem of a zone file name may be.
    #[error("a zone name cannot exceed 250 characters")]
    TooLong,

    /// Not a hostname a tenant may claim: the reason is the hostname check's own.
    #[error("not a valid zone name: {0}")]
    NotAHostName(#[source] DomainError),

    /// A single label, which would make the daemon authoritative for a whole
    /// top-level domain.
    #[error("a zone name needs at least two labels")]
    SingleLabel,
}
