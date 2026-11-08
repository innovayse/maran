//! The name a DKIM key is published under.

use super::dkim_selector_error::DkimSelectorError;

/// The longest selector, in characters. The selector is one DNS label
/// (`<selector>._domainkey.<domain>`) and a file name; 32 is far under both
/// limits and longer than any rotation scheme needs.
pub const MAXIMUM_LENGTH: usize = 32;

/// A validated DKIM selector: lower-case letters, digits and hyphens, starting
/// and ending with a letter or digit.
///
/// It becomes a DNS label, a key file name under a root-owned directory and a
/// field in the signing daemon's configuration. RFC 6376 permits dots and
/// upper case in a selector; this grammar does not, because a dot is a path
/// and label separator and case is not preserved by DNS — a smaller alphabet
/// has fewer ways to name a file the caller did not mean (rules/security.md
/// item 4).
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct DkimSelector(String);

impl DkimSelector {
    /// Validates `candidate` and wraps it. Case is NOT folded: `Selector1` is
    /// refused, not renamed, because the panel published the key under one exact
    /// spelling and a silent rename would sign under a name that is not in DNS.
    ///
    /// # Errors
    ///
    /// - [`DkimSelectorError::Empty`] for an empty candidate.
    /// - [`DkimSelectorError::TooLong`] beyond [`MAXIMUM_LENGTH`] characters.
    /// - [`DkimSelectorError::IllegalCharacter`] for anything but `a-z`, `0-9`
    ///   and `-`.
    /// - [`DkimSelectorError::EdgeNotAlphanumeric`] for a leading or trailing
    ///   hyphen.
    pub fn parse(candidate: &str) -> Result<Self, DkimSelectorError> {
        if candidate.is_empty() {
            return Err(DkimSelectorError::Empty);
        }

        if candidate.chars().count() > MAXIMUM_LENGTH {
            return Err(DkimSelectorError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }

        if let Some(character) = candidate
            .chars()
            .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit() || *c == '-'))
        {
            return Err(DkimSelectorError::IllegalCharacter { character });
        }

        if candidate.starts_with('-') || candidate.ends_with('-') {
            return Err(DkimSelectorError::EdgeNotAlphanumeric);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated selector.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/mail/dkim_selector_tests.rs"]
mod tests;
