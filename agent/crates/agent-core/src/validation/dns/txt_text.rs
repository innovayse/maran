//! The text of a TXT record.

use crate::validation::dns::first_refused_text_character::first_refused_text_character;
use crate::validation::dns::txt_text_error::TxtTextError;

/// The most one character-string of a TXT record can hold.
const CHUNK_BYTES: usize = 255;

/// A non-empty text of at most 2048 bytes drawn from the closed alphabet of
/// `first_refused_text_character`.
///
/// The text is rendered between quotes, in chunks of at most 255 bytes — the
/// limit of one DNS character-string — so a long DKIM key is several quoted
/// strings of one record, which resolvers concatenate. Because the alphabet is
/// ASCII, a chunk boundary can never fall inside a character.
///
/// Cost, stated: a legitimate TXT that needs a double quote or a backslash cannot
/// be published. SPF, DKIM, DMARC and domain-verification values never do.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TxtText(String);

impl TxtText {
    /// The longest text accepted, in bytes.
    pub const MAX_LENGTH: usize = 2048;

    /// Parses `candidate`.
    ///
    /// # Errors
    ///
    /// - [`TxtTextError::Empty`] for an empty text.
    /// - [`TxtTextError::TooLong`] beyond 2048 bytes.
    /// - [`TxtTextError::IllegalCharacter`] for any character outside the
    ///   alphabet, the first one found.
    pub fn parse(candidate: &str) -> Result<Self, TxtTextError> {
        if candidate.is_empty() {
            return Err(TxtTextError::Empty);
        }

        if candidate.len() > Self::MAX_LENGTH {
            return Err(TxtTextError::TooLong);
        }

        if let Some(character) = first_refused_text_character(candidate) {
            return Err(TxtTextError::IllegalCharacter { character });
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated text.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// The text split into pieces of at most 255 bytes, in order.
    #[must_use]
    pub fn chunks(&self) -> Vec<&str> {
        // ASCII only, so every byte offset is a character boundary.
        self.0
            .as_bytes()
            .chunks(CHUNK_BYTES)
            .filter_map(|piece| std::str::from_utf8(piece).ok())
            .collect()
    }
}

#[cfg(test)]
#[path = "../../tests/validation/dns/txt_text_tests.rs"]
mod tests;
