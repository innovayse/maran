//! A system user name for FTPS access, carrying its owning account's prefix.

use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::prefixed_name::{SEPARATOR, prefixed};
use crate::validation::system::ftps_user_name_error::FtpsUserNameError;
use crate::validation::system::name::AccountName;

/// The `useradd` name ceiling.
///
/// Thirty-two bytes, which is what `useradd` accepts on both supported families.
const MAXIMUM_LENGTH: usize = 32;

/// A validated FTPS system user name, always prefixed with the account that
/// owns it.
///
/// The system user namespace is global to the host, so the prefix serves the
/// same purpose it serves for the SFTP login and for MySQL: one tenant cannot
/// occupy or reach another tenant's login. It is applied by
/// [`FtpsUserName::for_account`] and nowhere else, and no unprefixed value can
/// be constructed — the inner `String` is private and this type has exactly two
/// constructors, the builder and its own inverse.
///
/// # What holding one of these proves
///
/// The name becomes a `useradd` argument, a path segment under the jail root,
/// and the FILE NAME of a per-user vsftpd configuration under
/// `user_config_dir`, whose contents are `key=value` lines. Both vsftpd's
/// configuration and the `/etc/passwd` line `useradd` writes are line-oriented,
/// so a newline in this value would append directives of the caller's choosing
/// to a file a root daemon reads (rules/security.md §4). The alphabet below is
/// what stops that, and it stops it the way `AccountName` stops log injection —
/// by the pattern being unable to express the character at all. Nothing
/// downstream escapes anything, and nothing needs to: there is no
/// `FtpsUserName` in the process whose text contains a byte outside
/// `[a-z0-9_]`.
///
/// The result also satisfies `useradd`'s own `NAME_REGEX`, `[a-z_][a-z0-9_-]*`:
/// the prefix is an `AccountName`, which already begins with a lowercase
/// letter, and every character after it is `[a-z0-9_]`.
///
/// # Why it is a separate type from `SftpUserName`
///
/// The two are the same shape and are not interchangeable. An FTPS login is a
/// member of the `maran-ftps` group and is refused by sshd's `Match Group`
/// block; an SFTP login is the reverse. Handing one where the other is expected
/// would create a login in the wrong group — a login that authenticates against
/// a daemon nobody meant to expose it to — so the compiler is made to refuse it
/// rather than a reviewer. What the two share is the prefixing and length rule,
/// and that is shared as code
/// (`validation::prefixed_name::prefixed`, crate-private), not by making one
/// type serve both purposes.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct FtpsUserName(String);

impl FtpsUserName {
    /// Builds the system user name that will actually be created, from the
    /// account that owns it and the name its customer asked for.
    ///
    /// The requested half is restricted to `[a-z0-9]`, which excludes the
    /// separator: `AccountName` permits underscores, so a suffix containing one
    /// would let account `alice` request `bob_deploy` and be handed
    /// `alice_bob_deploy`, which reads as `bob`'s login in `/etc/passwd`.
    ///
    /// The excluded set is therefore not a list of characters somebody thought
    /// to ban: it is everything except twenty-six letters and ten digits, which
    /// is why a newline, a carriage return, a `.`, a `/`, a `..`, a leading `-`
    /// and every control character are all refused by the same clause, each
    /// naming the first offending character.
    ///
    /// # Errors
    ///
    /// - [`FtpsUserNameError::Empty`] when nothing was requested.
    /// - [`FtpsUserNameError::UnexpectedCharacter`] for anything outside
    ///   `[a-z0-9]`, the separator included.
    /// - [`FtpsUserNameError::TooLong`] when the prefixed result exceeds the
    ///   thirty-two byte `useradd` limit.
    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, FtpsUserNameError> {
        prefixed(account, requested, MAXIMUM_LENGTH)
            .map(Self)
            .map_err(|problem| match problem {
                PrefixProblem::Empty => FtpsUserNameError::Empty,
                PrefixProblem::UnexpectedCharacter { character } => {
                    FtpsUserNameError::UnexpectedCharacter { character }
                }
                PrefixProblem::TooLong { length } => FtpsUserNameError::TooLong { length },
            })
    }

    /// Decodes a full system login back into the name, only when it belongs to
    /// `account`.
    ///
    /// The inverse of [`FtpsUserName::for_account`], kept on the same type so
    /// the separator cannot drift between the builder and the decoder. The
    /// WHOLE account is compared, not a prefix of it: account names may contain
    /// the separator, so `alice_` is a prefix of `alice_bob_deploy`, which
    /// belongs to account `alice_bob`. Splitting at the LAST separator recovers
    /// the halves, because `for_account` forbids the separator in the requested
    /// half.
    ///
    /// A candidate this agent could not have created — `root`, a login an
    /// administrator made by hand, another account's — decodes to `None` rather
    /// than to a plausible-looking guess. That refusal is what an account
    /// deletion enumerating logins to remove depends on: it is the difference
    /// between removing this account's FTPS logins and removing whatever else
    /// in `/etc/passwd` happened to start with the same letters.
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
#[path = "../../tests/validation/system/ftps_user_name_tests.rs"]
mod tests;
