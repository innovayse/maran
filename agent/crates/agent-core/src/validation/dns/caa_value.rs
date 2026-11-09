//! The value of a CAA record.

use crate::validation::dns::caa_value_error::CaaValueError;
use crate::validation::dns::first_refused_text_character::first_refused_text_character;

/// A non-empty value of at most 255 bytes from the same closed alphabet as a TXT
/// text, written between quotes.
///
/// Its meaning depends on the tag (an issuer name with optional parameters, or a
/// reporting URL), and whether it fits the tag is the daemon's checker's to say;
/// what this type guarantees is that it can only ever be data inside quotes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CaaValue(String);

impl CaaValue {
    /// The longest value accepted, in bytes.
    pub const MAX_LENGTH: usize = 255;

    /// Parses `candidate`.
    ///
    /// # Errors
    ///
    /// - [`CaaValueError::Empty`] for an empty value.
    /// - [`CaaValueError::TooLong`] beyond 255 bytes.
    /// - [`CaaValueError::IllegalCharacter`] for a character outside the alphabet.
    pub fn parse(candidate: &str) -> Result<Self, CaaValueError> {
        if candidate.is_empty() {
            return Err(CaaValueError::Empty);
        }

        if candidate.len() > Self::MAX_LENGTH {
            return Err(CaaValueError::TooLong);
        }

        if let Some(character) = first_refused_text_character(candidate) {
            return Err(CaaValueError::IllegalCharacter { character });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated value.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/caa_value_tests.rs"]
mod tests;
