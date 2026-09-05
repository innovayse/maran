//! A MySQL user name that carries its owning account's prefix.

use crate::validation::db::db_user_name_error::DbUserNameError;
use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::prefixed_name::{SEPARATOR, prefixed};
use crate::validation::system::name::AccountName;

/// MySQL's user-name ceiling.
///
/// Thirty-two bytes. It matters more than the database limit does, because older
/// servers **truncate** a longer name instead of refusing it — and a truncated
/// name is how two tenants silently end up sharing one MySQL account. Refusing
/// here is the only way to be sure the name the panel recorded is the name the
/// server created.
const MAXIMUM_LENGTH: usize = 32;

/// A validated MySQL user name, always prefixed with the account that owns it.
///
/// MySQL's user namespace is global to the server exactly as its database
/// namespace is, so the same reasoning applies: the prefix is applied by
/// [`DbUserName::for_account`] and nowhere else, and no unprefixed value can be
/// constructed.
///
/// The name is interpolated into `CREATE USER '<name>'@'localhost'`, which MySQL
/// cannot parameterise. What makes that safe is the alphabet — a `DbUserName`
/// cannot hold a quote, a backslash, a space or a newline — not any escaping
/// (rules/rust.md "Validation first").
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct DbUserName(String);

impl DbUserName {
    /// Builds the user name MySQL will actually hold, from the account that owns
    /// it and the name its customer asked for.
    ///
    /// The requested half is restricted to `[a-z0-9]`, which excludes the
    /// separator: `AccountName` permits underscores, so a suffix that could
    /// contain one would let account `alice` request `bob_admin` and be handed
    /// `alice_bob_admin`, a name that reads as `bob`'s in every grant listing.
    ///
    /// # Errors
    ///
    /// - [`DbUserNameError::Empty`] when nothing was requested.
    /// - [`DbUserNameError::UnexpectedCharacter`] for anything outside
    ///   `[a-z0-9]`, the separator included.
    /// - [`DbUserNameError::TooLong`] when the prefixed result exceeds MySQL's
    ///   thirty-two byte user-name limit.
    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, DbUserNameError> {
        prefixed(account, requested, MAXIMUM_LENGTH)
            .map(Self)
            .map_err(|problem| match problem {
                PrefixProblem::Empty => DbUserNameError::Empty,
                PrefixProblem::UnexpectedCharacter { character } => {
                    DbUserNameError::UnexpectedCharacter { character }
                }
                PrefixProblem::TooLong { length } => DbUserNameError::TooLong { length },
            })
    }

    /// Decodes a database user name back into the type, only when it belongs
    /// to `account`.
    ///
    /// The inverse of [`DbUserName::for_account`], kept on the same type so the
    /// separator cannot drift between the builder and the decoder. The WHOLE
    /// account is compared, not a prefix of it: account names may contain the
    /// separator, so `alice_` is a prefix of `alice_bob_admin`, which belongs
    /// to account `alice_bob`. Splitting at the LAST separator recovers the
    /// halves, because `for_account` forbids the separator in the requested
    /// half.
    ///
    /// A candidate this agent could not have created — `root`, `mysql.sys`, a
    /// user an administrator made by hand, another account's — decodes to
    /// `None`. It never comes back as a plausible-looking guess: this is what
    /// a cascade deciding which credentials to drop reads.
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

    /// The name as MySQL will hold it, prefix included.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/db/db_user_name_tests.rs"]
mod tests;
