//! The subject line of an out-of-office reply.

use super::autoresponder_subject_error::AutoresponderSubjectError;
use super::is_text_reordering::is_text_reordering;

/// The longest subject, in characters.
pub const MAXIMUM_LENGTH: usize = 200;

/// A validated autoresponder subject: one line of text with no character the
/// script language gives a meaning inside a quoted string.
///
/// It is placed in the Sieve script as `:subject "<text>"`. A quoted string in
/// that language ends at an unescaped double quote and gives a backslash an
/// escaping role, so a subject holding either could close the string and
/// continue as script. Instead of escaping them (and trusting the escaping to
/// be right), neither is accepted: the text can then be written verbatim and
/// there is nothing to get wrong (rules/security.md item 4). `${` is refused
/// as well, for the reason the body refuses it: it is inert unless the script
/// enables variables, and keeping it out means a later change to the script
/// cannot turn a customer's text into an expression. A control character is
/// refused for the same reason it is refused everywhere: a line break ends the
/// line.
///
/// Non-ASCII text is accepted — a subject in any language is ordinary — except
/// for the characters that reorder or hide text, which have no use in a
/// subject and a use in disguising one.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AutoresponderSubject(String);

impl AutoresponderSubject {
    /// Validates `candidate` and wraps it unchanged.
    ///
    /// # Errors
    ///
    /// - [`AutoresponderSubjectError::Empty`] for an empty candidate.
    /// - [`AutoresponderSubjectError::TooLong`] beyond [`MAXIMUM_LENGTH`]
    ///   characters.
    /// - [`AutoresponderSubjectError::ControlCharacter`] for a control or
    ///   text-reordering character; checked before the quoting characters.
    /// - [`AutoresponderSubjectError::QuotingCharacter`] for `"` or `\`.
    /// - [`AutoresponderSubjectError::VariableExpansion`] for `${`.
    pub fn parse(candidate: &str) -> Result<Self, AutoresponderSubjectError> {
        if candidate.is_empty() {
            return Err(AutoresponderSubjectError::Empty);
        }

        if candidate.chars().count() > MAXIMUM_LENGTH {
            return Err(AutoresponderSubjectError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        if candidate
            .chars()
            .any(|c| c.is_control() || is_text_reordering(c))
        {
            return Err(AutoresponderSubjectError::ControlCharacter);
        }

        if let Some(character) = candidate.chars().find(|c| matches!(c, '"' | '\\')) {
            return Err(AutoresponderSubjectError::QuotingCharacter { character });
        }

        if candidate.contains("${") {
            return Err(AutoresponderSubjectError::VariableExpansion);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated subject.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/autoresponder_subject_tests.rs"]
mod tests;
