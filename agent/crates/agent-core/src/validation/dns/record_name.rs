//! The owner of a record, relative to its zone.

use crate::validation::dns::label_fault::LabelFault;
use crate::validation::dns::label_fault_of::label_fault_of;
use crate::validation::dns::record_name_error::RecordNameError;
use crate::validation::dns::zone_name::ZoneName;

/// The longest absolute name DNS allows, without the trailing dot.
const MAX_QUALIFIED_LENGTH: usize = 253;

/// Where in a zone a record sits: the apex itself, or a name below it.
///
/// Relative by construction: the wire spells the apex `@` and everything else
/// without a trailing dot, so an absolute name pointing outside the zone has no
/// spelling that parses. Labels are ASCII letters, digits, `-` and `_`; the
/// leftmost label alone may be a lone `*` (a wildcard). Lowercased.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub enum RecordName {
    /// The zone's own name (`@`).
    Apex,
    /// A name below the apex, as labels joined by dots, no trailing dot.
    Relative(String),
}

impl RecordName {
    /// Parses `candidate`.
    ///
    /// # Errors
    ///
    /// - [`RecordNameError::Empty`] for an empty name.
    /// - [`RecordNameError::Absolute`] for a name with a trailing dot.
    /// - [`RecordNameError::TooLong`] beyond 253 characters.
    /// - [`RecordNameError::InvalidLabel`] for an empty or over-long label or a
    ///   hyphen at its edge.
    /// - [`RecordNameError::IllegalCharacter`] for a character outside the
    ///   alphabet (a newline, a space, `;`, `"`, `\`, `/`, `$`, NUL).
    /// - [`RecordNameError::MisplacedWildcard`] for a `*` anywhere but alone as the
    ///   leftmost label.
    pub fn parse(candidate: &str) -> Result<Self, RecordNameError> {
        if candidate.is_empty() {
            return Err(RecordNameError::Empty);
        }

        if candidate == "@" {
            return Ok(Self::Apex);
        }

        if candidate.ends_with('.') {
            return Err(RecordNameError::Absolute);
        }

        if candidate.len() > MAX_QUALIFIED_LENGTH {
            return Err(RecordNameError::TooLong);
        }

        for (index, label) in candidate.split('.').enumerate() {
            if label == "*" {
                if index != 0 {
                    return Err(RecordNameError::MisplacedWildcard);
                }
                continue;
            }
            if label.contains('*') {
                return Err(RecordNameError::MisplacedWildcard);
            }
            match label_fault_of(label) {
                Some(LabelFault::IllegalCharacter(character)) => {
                    return Err(RecordNameError::IllegalCharacter { character });
                }
                Some(_) => {
                    return Err(RecordNameError::InvalidLabel {
                        label: label.chars().take(63).collect(),
                    });
                }
                None => {}
            }
        }

        Ok(Self::Relative(candidate.to_ascii_lowercase()))
    }

    /// The name as it is written in the owner column: `@` or the relative labels.
    #[must_use]
    pub fn owner_text(&self) -> &str {
        match self {
            Self::Apex => "@",
            Self::Relative(labels) => labels,
        }
    }

    /// Whether this is the apex.
    #[must_use]
    pub fn is_apex(&self) -> bool {
        matches!(self, Self::Apex)
    }

    /// Confirms the name stays within DNS's 253 characters once `zone` is
    /// appended.
    ///
    /// # Errors
    ///
    /// [`RecordNameError::TooLongInZone`] when it does not.
    pub fn fits_within(&self, zone: &ZoneName) -> Result<(), RecordNameError> {
        let length = match self {
            Self::Apex => zone.as_str().len(),
            Self::Relative(labels) => labels.len() + 1 + zone.as_str().len(),
        };

        if length > MAX_QUALIFIED_LENGTH {
            return Err(RecordNameError::TooLongInZone);
        }

        Ok(())
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/record_name_tests.rs"]
mod tests;
