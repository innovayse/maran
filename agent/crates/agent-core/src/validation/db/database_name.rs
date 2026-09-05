//! A MySQL database name that carries its owning account's prefix.

use crate::validation::db::database_name_error::DatabaseNameError;
use crate::validation::prefix_problem::PrefixProblem;
use crate::validation::prefixed_name::{SEPARATOR, prefixed};
use crate::validation::system::name::AccountName;

/// MySQL's identifier ceiling.
///
/// Sixty-four **bytes**, not characters — but the allow-list below is ASCII-only,
/// so for this type the two are the same number.
const MAXIMUM_LENGTH: usize = 64;

/// A validated MySQL database name, always prefixed with the account that owns it.
///
/// MySQL's database namespace is global to the server: there is one `wordpress`
/// for the whole host, not one per tenant. A bare name therefore lets the first
/// tenant to ask occupy a name every other tenant then cannot use — or, worse,
/// be handed a database another tenant already filled. The prefix is applied by
/// [`DatabaseName::for_account`] and by nothing else, and the inner string is
/// private, so an unprefixed value cannot be constructed at all. That is the
/// point: a check in a handler is one refactor away from being skipped, a
/// constructor that cannot produce the unsafe value is not.
///
/// The name is also interpolated straight into ``CREATE DATABASE `name` `` —
/// MySQL's DDL cannot parameterise an identifier, so there is nowhere to bind it.
/// What makes that safe is the alphabet, not any escaping: a `DatabaseName`
/// cannot hold a backtick, a quote, a semicolon, a space or a newline, so there
/// is nothing for an interpolation to break out with (rules/rust.md
/// "Validation first" — values are VALIDATED, not escaped).
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct DatabaseName(String);

impl DatabaseName {
    /// Builds the name a database will actually have, from the account that owns
    /// it and the name its customer asked for.
    ///
    /// The requested half is restricted to `[a-z0-9]`, which excludes the
    /// separator itself. That exclusion is not tidiness: `AccountName` permits
    /// underscores, so if the suffix could contain one, account `alice` asking
    /// for `bob_secrets` would produce `alice_bob_secrets` — a name that reads as
    /// belonging to account `bob` in every listing, log line and backup file an
    /// operator will ever look at.
    ///
    /// # Errors
    ///
    /// - [`DatabaseNameError::Empty`] when nothing was requested.
    /// - [`DatabaseNameError::UnexpectedCharacter`] for anything outside
    ///   `[a-z0-9]`, the separator included.
    /// - [`DatabaseNameError::TooLong`] when the prefixed result exceeds MySQL's
    ///   sixty-four byte identifier limit.
    pub fn for_account(account: &AccountName, requested: &str) -> Result<Self, DatabaseNameError> {
        prefixed(account, requested, MAXIMUM_LENGTH)
            .map(Self)
            .map_err(|problem| match problem {
                PrefixProblem::Empty => DatabaseNameError::Empty,
                PrefixProblem::UnexpectedCharacter { character } => {
                    DatabaseNameError::UnexpectedCharacter { character }
                }
                PrefixProblem::TooLong { length } => DatabaseNameError::TooLong { length },
            })
    }

    /// Decodes a database name back into the type, only when it belongs to
    /// `account`.
    ///
    /// The inverse of [`DatabaseName::for_account`], kept on the same type so
    /// the separator cannot drift between the builder and the decoder. The
    /// WHOLE account is compared, not a prefix of it: account names may contain
    /// the separator, so `alice_` is a prefix of `alice_bob_shop`, which
    /// belongs to account `alice_bob`. Splitting at the LAST separator recovers
    /// the halves, because `for_account` forbids the separator in the requested
    /// half.
    ///
    /// A candidate this agent could not have created — `mysql`,
    /// `information_schema`, a database an administrator made by hand, another
    /// account's — decodes to `None` rather than to a plausible-looking guess.
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
#[path = "../../tests/validation/db/database_name_tests.rs"]
mod tests;
