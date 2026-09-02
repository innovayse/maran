//! A hostname that is safe to write into a web-server configuration.

use crate::validation::web::domain_error::DomainError;

/// The longest a domain may be, from DNS.
const MAX_LENGTH: usize = 253;

/// The longest a single label may be, from DNS.
const MAX_LABEL_LENGTH: usize = 63;

/// A syntactically valid hostname, checked once at the boundary and then
/// carried as a type so no later caller has to remember to check it again.
///
/// Construction is the only way to obtain one, so a `Domain` in a signature is
/// a promise that the value has been through [`Domain::parse`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Domain(String);

impl Domain {
    /// Parses `candidate` into a domain.
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
    pub fn parse(candidate: &str) -> Result<Self, DomainError> {
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
