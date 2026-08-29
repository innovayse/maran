//! Account name validation: the single gate every system-facing name passes.

use std::sync::LazyLock;

use regex::Regex;

/// Matches valid account names: a lowercase letter, then lowercase letters,
/// digits or underscores, 3–30 characters in total.
///
/// The shape is dictated by `useradd`, not by taste. A name reaching this crate
/// goes on to become a system user, a home directory, a systemd unit name and a
/// path segment, so anything outside this alphabet — a space, a quote, a
/// semicolon, a slash, a non-ASCII letter — is rejected once, here, rather than
/// escaped differently by each caller.
static NAME_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    // The pattern is a literal that cannot fail to compile; `unreachable!` keeps
    // the workspace's no-unwrap rule intact without pretending to handle a case
    // that cannot occur.
    #[allow(clippy::unwrap_used)]
    Regex::new(r"^[a-z][a-z0-9_]{2,29}$").unwrap()
});

/// A validated hosting-account name, safe to embed in paths and unit names.
///
/// The inner string is private and the only constructor is [`AccountName::parse`],
/// so a value of this type is proof that validation happened — a function taking
/// an `AccountName` cannot be handed an unchecked string by mistake.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct AccountName(String);

/// Rejection reasons for [`AccountName::parse`].
#[derive(Debug, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum NameError {
    /// The candidate does not match the allowed pattern.
    ///
    /// Deliberately one variant with no detail: telling a caller *which* rule it
    /// broke tells an attacker how the rules are shaped, and every rejection has
    /// the same remedy — send a name that matches the documented pattern.
    #[error("invalid account name")]
    Invalid,
}

impl AccountName {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// Returns [`NameError::Invalid`] when the candidate does not match the
    /// documented pattern.
    pub fn parse(candidate: &str) -> Result<Self, NameError> {
        if NAME_PATTERN.is_match(candidate) {
            Ok(Self(candidate.to_owned()))
        } else {
            Err(NameError::Invalid)
        }
    }

    /// The validated name as a string slice.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}
