//! A MySQL database name that carries its owning account's prefix.

use crate::validation::db::database_name_error::DatabaseNameError;
use crate::validation::system::name::AccountName;

/// The separator between an account's name and the name its customer chose.
///
/// An underscore, because MySQL accepts it in an unquoted identifier and because
/// `<account>_<name>` is the form operators already read in listings without
/// having to be told what the first half means.
const SEPARATOR: char = '_';

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
        if requested.is_empty() {
            return Err(DatabaseNameError::Empty);
        }

        if let Some(character) = requested
            .chars()
            .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
        {
            return Err(DatabaseNameError::UnexpectedCharacter { character });
        }

        let full = format!("{}{SEPARATOR}{requested}", account.as_str());
        if full.len() > MAXIMUM_LENGTH {
            return Err(DatabaseNameError::TooLong { length: full.len() });
        }

        Ok(Self(full))
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
