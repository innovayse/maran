//! The text of an out-of-office reply.

use super::autoresponder_body_error::AutoresponderBodyError;
use super::is_text_reordering::is_text_reordering;

/// The longest body, in bytes.
pub const MAXIMUM_LENGTH: usize = 4096;

/// A validated autoresponder body: UTF-8 text of several lines with no
/// character the script language gives a meaning inside its multi-line text.
///
/// It is placed in the Sieve script as a `text:` literal, which ends at a line
/// consisting of a single dot. Dot-stuffing (doubling a leading dot) would
/// escape it, and an escape is a thing to get wrong; a body with a line
/// beginning with a dot is refused instead, so the text is written verbatim
/// and nothing in it can end the literal. `${` is refused as well: it is
/// inert unless the script enables variables, and keeping it out means a later
/// change to the script cannot turn a customer's text into an expression
/// (rules/security.md item 4).
///
/// Only the line feed is accepted as a control character: it is the line
/// separator. A carriage return, a tab and every other control character
/// change what a line is.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AutoresponderBody(String);

impl AutoresponderBody {
    /// Validates `candidate` and wraps it unchanged.
    ///
    /// # Errors
    ///
    /// - [`AutoresponderBodyError::Empty`] for an empty candidate.
    /// - [`AutoresponderBodyError::TooLong`] beyond [`MAXIMUM_LENGTH`] bytes.
    /// - [`AutoresponderBodyError::ControlCharacter`] for a control character
    ///   other than `\n`, or a text-reordering one.
    /// - [`AutoresponderBodyError::LeadingDot`] for a line starting with `.`.
    /// - [`AutoresponderBodyError::VariableExpansion`] for `${`.
    pub fn parse(candidate: &str) -> Result<Self, AutoresponderBodyError> {
        if candidate.is_empty() {
            return Err(AutoresponderBodyError::Empty);
        }

        if candidate.len() > MAXIMUM_LENGTH {
            return Err(AutoresponderBodyError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        if candidate
            .chars()
            .any(|c| (c.is_control() && c != '\n') || is_text_reordering(c))
        {
            return Err(AutoresponderBodyError::ControlCharacter);
        }

        if candidate.split('\n').any(|line| line.starts_with('.')) {
            return Err(AutoresponderBodyError::LeadingDot);
        }

        if candidate.contains("${") {
            return Err(AutoresponderBodyError::VariableExpansion);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated body.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/autoresponder_body_tests.rs"]
mod tests;
