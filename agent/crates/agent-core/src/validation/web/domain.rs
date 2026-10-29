//! A hostname that is safe to write into a web-server configuration and that a
//! tenant is allowed to serve: well-formed, and not in the reserved set.

use crate::validation::web::domain_error::DomainError;
use crate::validation::web::reserved_host_name::reservation_of;

/// The longest a domain may be, from DNS.
const MAX_LENGTH: usize = 253;

/// The longest a single label may be, from DNS.
const MAX_LABEL_LENGTH: usize = 63;

/// A syntactically valid, non-reserved hostname, checked once at the boundary and then
/// carried as a type so no later caller has to remember to check it again.
///
/// Construction is the only way to obtain one, so a `Domain` in a signature is
/// a promise that the value has been through [`Domain::parse`] or, for a name
/// that may already exist, [`Domain::parse_existing`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Domain(String);

impl Domain {
    /// Parses `candidate` into a domain a tenant may CLAIM: well-formed and not
    /// in the reserved set. Use it wherever a name is about to be created or
    /// served (a new site, its aliases, an FTPS hostname).
    ///
    /// Lowercased on the way in, because DNS is case-insensitive while a
    /// config file and a filesystem path are not: two sites differing only in
    /// case must not become two different document roots.
    ///
    /// # Errors
    ///
    /// Every error of [`Domain::parse_existing`], and in addition:
    ///
    /// - [`DomainError::NumericFinalLabel`] when the last label is all digits
    ///   (an IPv4 literal such as `203.0.113.7`, `1.2.3`, or a lone `12345`).
    /// - [`DomainError::LocalhostFamily`] for `localhost` and `*.localhost`.
    /// - [`DomainError::SpecialUseSuffix`] for a name that is, or ends in,
    ///   `local`, `localdomain`, `internal`, `lan`, `home.arpa` or `invalid`.
    ///
    /// A plain single label such as `shop` stays accepted: only the reserved set
    /// above is refused, and the API's own rule is what demands two labels.
    pub fn parse(candidate: &str) -> Result<Self, DomainError> {
        let existing = Self::parse_existing(candidate)?;

        if let Some(reserved) = reservation_of(existing.as_str()) {
            return Err(reserved);
        }

        Ok(existing)
    }

    /// Parses the name of a site or host that may ALREADY exist: the shape
    /// check alone, without the reserved set.
    ///
    /// A tenant could create a now-reserved name before the set was enforced, and
    /// the agent must still be able to disable, delete, inspect and tail such a
    /// site, or the clean-up of the very hole the set closes would be refused.
    /// Never use it for a name about to be created or served: that is
    /// [`Domain::parse`].
    ///
    /// Lowercased on the way in, because DNS is case-insensitive while a
    /// config file and a filesystem path are not: two sites differing only in
    /// case must not become two different document roots.
    ///
    /// # Errors
    ///
    /// - [`DomainError::Empty`] when `candidate` is empty.
    /// - [`DomainError::TooLong`] beyond 253 characters.
    /// - [`DomainError::IllegalCharacter`] for anything but ASCII letters,
    ///   digits, `-` and `.` — which is what rejects the newline that would
    ///   otherwise end the `server_name` line and start a directive of the
    ///   caller's choosing.
    /// - [`DomainError::InvalidLabel`] for an empty or over-long label, or one
    ///   starting or ending with a hyphen.
    pub fn parse_existing(candidate: &str) -> Result<Self, DomainError> {
        if candidate.is_empty() {
            return Err(DomainError::Empty);
        }

        if candidate.len() > MAX_LENGTH {
            return Err(DomainError::TooLong);
        }

        if let Some(character) = candidate
            .chars()
            .find(|c| !c.is_ascii_alphanumeric() && *c != '-' && *c != '.')
        {
            return Err(DomainError::IllegalCharacter { character });
        }

        for label in candidate.split('.') {
            if label.is_empty()
                || label.len() > MAX_LABEL_LENGTH
                || label.starts_with('-')
                || label.ends_with('-')
            {
                return Err(DomainError::InvalidLabel {
                    label: label.to_owned(),
                });
            }
        }

        Ok(Self(candidate.to_ascii_lowercase()))
    }

    /// The validated domain.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/domain_tests.rs"]
mod tests;
