//! A system user name for SFTP access, carrying its owning account's prefix.

use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::prefixed_name::{SEPARATOR, prefixed};
use crate::validation::system::name::AccountName;
use crate::validation::system::sftp_user_name_error::SftpUserNameError;

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
        prefixed(account, requested, MAXIMUM_LENGTH)
            .map(Self)
            .map_err(|problem| match problem {
                PrefixProblem::Empty => SftpUserNameError::Empty,
                PrefixProblem::UnexpectedCharacter { character } => {
                    SftpUserNameError::UnexpectedCharacter { character }
                }
                PrefixProblem::TooLong { length } => SftpUserNameError::TooLong { length },
            })
    }

    /// Decodes a full system login back into the name, only when it belongs to
    /// `account`.
    ///
    /// The inverse of [`SftpUserName::for_account`], kept on the same type so
    /// the separator cannot drift between the builder and the decoder. The
    /// WHOLE account is compared, not a prefix of it: account names may contain
    /// the separator, so `alice_` is a prefix of `alice_bob_deploy`, which
    /// belongs to account `alice_bob`. Splitting at the LAST separator recovers
    /// the halves, because `for_account` forbids the separator in the requested
    /// half.
    ///
    /// A candidate this agent could not have created — `root`, a login an
    /// administrator made by hand, another account's — decodes to `None`
    /// rather than to a plausible-looking guess. That refusal is what an
    /// account deletion enumerating logins to remove depends on.
    #[must_use]
    pub fn decode(account: &AccountName, candidate: &str) -> Option<Self> {
        let (owner, requested) = candidate.rsplit_once(SEPARATOR)?;
        if owner != account.as_str() {
            return None;
        }

        // Rebuilt rather than wrapped: `for_account` is the only constructor,
        // which keeps every value in the process one this agent could create.
        Self::for_account(account, requested).ok()
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
