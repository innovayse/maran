//! A MySQL user name that carries its owning account's prefix.

use crate::validation::db::db_user_name_error::DbUserNameError;
use crate::validation::system::name::AccountName;

/// The separator between an account's name and the name its customer chose.
///
/// The same underscore [`super::database_name::DatabaseName`] uses, so a
/// database and the user that owns it read as one pair in an operator's listing.
const SEPARATOR: char = '_';

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
        if requested.is_empty() {
            return Err(DbUserNameError::Empty);
        }

        if let Some(character) = requested
            .chars()
            .find(|c| !(c.is_ascii_lowercase() || c.is_ascii_digit()))
        {
            return Err(DbUserNameError::UnexpectedCharacter { character });
        }

        let full = format!("{}{SEPARATOR}{requested}", account.as_str());
        if full.len() > MAXIMUM_LENGTH {
            return Err(DbUserNameError::TooLong { length: full.len() });
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
#[path = "../../tests/validation/db/db_user_name_tests.rs"]
mod tests;
