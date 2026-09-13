//! Account name validation: the single gate every system-facing name passes.

use super::name_error::NameError;

/// Shortest accepted name, matching what `useradd` will take.
const MIN_LENGTH: usize = 3;

/// Longest accepted name, matching the conventional `useradd` limit.
const MAX_LENGTH: usize = 30;

/// A validated hosting-account name, safe to embed in paths and unit names.
///
/// The inner string is private and the only constructor is [`AccountName::parse`],
/// so holding a value of this type is proof that validation happened — a function
/// taking an `AccountName` cannot be handed an unchecked string by mistake.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct AccountName(String);

impl AccountName {
    /// Validates `candidate` and wraps it.
    ///
    /// Accepts a lowercase ASCII letter followed by lowercase ASCII letters,
    /// digits or underscores, between 3 and 30 characters in total — the bounds
    /// `useradd` itself accepts.
    ///
    /// The alphabet is dictated by `useradd`, not by taste. A name reaching this
    /// crate goes on to become a system user, a home directory, a systemd unit
    /// name and a path segment, so anything outside it — a space, a quote, a
    /// semicolon, a slash, a non-ASCII letter — is rejected once, here, rather
    /// than escaped differently by each caller.
    ///
    /// Written as explicit character checks rather than a regex: the rule is
    /// short enough to read directly, and this way the crate needs neither a
    /// regex dependency nor the lazily-compiled pattern whose "this literal
    /// cannot fail to compile" unwrap the agent is not allowed to write.
    ///
    /// # Errors
    ///
    /// Returns [`NameError::Invalid`] when the candidate does not match.
    pub fn parse(candidate: &str) -> Result<Self, NameError> {
        // Counted in bytes, which equals characters here because every accepted
        // character is ASCII — anything else fails the alphabet check below.
        if !(MIN_LENGTH..=MAX_LENGTH).contains(&candidate.len()) {
            return Err(NameError::Invalid);
        }

        let mut characters = candidate.chars();
        let starts_correctly = characters
            .next()
            .is_some_and(|first| first.is_ascii_lowercase());
        if !starts_correctly {
            return Err(NameError::Invalid);
        }

        let rest_is_allowed = characters.all(|character| {
            character.is_ascii_lowercase() || character.is_ascii_digit() || character == '_'
        });
        if !rest_is_allowed {
            return Err(NameError::Invalid);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated name as a string slice.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/name_tests.rs"]
mod tests;
