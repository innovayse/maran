//! The login the panel uses on a remote migration source.

use super::source_user_name_error::SourceUserNameError;

/// The longest login name Linux accepts.
const MAX_LENGTH: usize = 32;

/// A login name on a remote server an administrator registered as a migration
/// source: `[a-z_][a-z0-9_-]{0,31}`.
///
/// It becomes the `user` of `ssh user@host`, and for the inventory it is also
/// spliced into one remote command (`crontab -l -u <user>`), so the grammar is
/// the portable Linux login grammar and nothing wider: no `@`, no space, no `/`,
/// no uppercase and no leading `-`. A remote server's own accounts are validated
/// by the same type before the agent asks about their crontabs.
///
/// Distinct from [`crate::validation::system::name::AccountName`] on purpose: an
/// account name is a hosting account on THIS host (prefix, length and character
/// rules of the panel), while a login on somebody else's server follows that
/// server's rules, of which `root` and names such as `_apt` are legitimate.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct SourceUserName(String);

impl SourceUserName {
    /// Parses `candidate` as a remote login name.
    ///
    /// # Errors
    ///
    /// - [`SourceUserNameError::Empty`] when `candidate` is empty.
    /// - [`SourceUserNameError::TooLong`] beyond 32 bytes.
    /// - [`SourceUserNameError::BadFirstCharacter`] unless the first character is
    ///   `a-z` or `_`.
    /// - [`SourceUserNameError::IllegalCharacter`] for any later character
    ///   outside `a-z`, `0-9`, `_` and `-` (a newline and a space included).
    pub fn parse(candidate: &str) -> Result<Self, SourceUserNameError> {
        let mut bytes = candidate.bytes();
        let first = bytes.next().ok_or(SourceUserNameError::Empty)?;
        if candidate.len() > MAX_LENGTH {
            return Err(SourceUserNameError::TooLong { limit: MAX_LENGTH });
        }
        if !(first.is_ascii_lowercase() || first == b'_') {
            return Err(SourceUserNameError::BadFirstCharacter);
        }
        if !bytes.all(|byte| {
            byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'_' || byte == b'-'
        }) {
            return Err(SourceUserNameError::IllegalCharacter);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated login name.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/source_user_name_tests.rs"]
mod tests;
