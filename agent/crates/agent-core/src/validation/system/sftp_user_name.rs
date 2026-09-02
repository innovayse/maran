//! A system user name for SFTP access, carrying its owning account's prefix.

use crate::validation::system::name::AccountName;
use crate::validation::system::sftp_user_name_error::SftpUserNameError;

/// The separator between an account's name and the name its customer chose.
///
/// The same underscore the database types use, so one hosting account's system
/// users, databases and database users all read as one family.
const SEPARATOR: char = '_';

/// The `useradd` name ceiling.
///
/// Thirty-two bytes, which is what `useradd` accepts on both supported families.
const MAXIMUM_LENGTH: usize = 32;

/// A validated OpenSSH/SFTP system user name, always prefixed with the account
/// that owns it.
///
/// The system user namespace is global to the host, so the prefix serves the
/// same purpose it serves for MySQL: one tenant cannot occupy or reach another
/// tenant's login. It is applied by [`SftpUserName::for_account`] and nowhere
/// else, and no unprefixed value can be constructed.
///
/// The name becomes a `useradd` argument, a home directory path segment and a
/// `Match User` line in an `sshd_config` drop-in. `sshd_config` is
/// line-oriented, so a newline in this value would append directives of the
/// caller's choosing to the SSH daemon's configuration (rules/security.md §4).
/// The alphabet below is what stops that; nothing downstream escapes anything.
///
/// The result also satisfies `useradd`'s own `NAME_REGEX`,
/// `[a-z_][a-z0-9_-]*`: the prefix is an `AccountName`, which already begins
/// with a lowercase letter, and every character after it is `[a-z0-9_]`.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct SftpUserName(String);

impl SftpUserName {
    /// Builds the system user name that will actually be created, from the
    /// account that owns it and the name its customer asked for.
    ///
    /// The requested half is restricted to `[a-z0-9]`, which excludes the
    /// separator: `AccountName` permits underscores, so a suffix containing one
    /// would let account `alice` request `bob_deploy` and be handed
    /// `alice_bob_deploy`, which reads as `bob`'s login in `/etc/passwd`.
    ///
    /// # Errors
    ///
    /// - [`SftpUserNameError::Empty`] when nothing was requested.
    /// - [`SftpUserNameError::UnexpectedCharacter`] for anything outside
    ///   `[a-z0-9]`, the separator included.
    /// - [`SftpUserNameError::TooLong`] when the prefixed result exceeds the
    ///   thirty-two byte `useradd` limit.
    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, SftpUserNameError> {
        if requested.is_empty() {
            return Err(SftpUserNameError::Empty);
        }

        if let Some(character) = requested
            .chars()
            .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
        {
            return Err(SftpUserNameError::UnexpectedCharacter { character });
        }

        let full = format!("{}{SEPARATOR}{requested}", account.as_str());
        if full.len() > MAXIMUM_LENGTH {
            return Err(SftpUserNameError::TooLong { length: full.len() });
        }

        Ok(Self(full))
    }

    /// The name as the system will hold it, prefix included.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/system/sftp_user_name_tests.rs"]
mod tests;
