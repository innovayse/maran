//! The name of a database on a registered migration source.

use super::remote_database_name_error::RemoteDatabaseNameError;

/// MySQL's identifier ceiling in characters; the alphabet below is ASCII, so it
/// is also the ceiling in bytes.
const MAXIMUM_LENGTH: usize = 64;

/// A validated name of a database that lives on ANOTHER server.
///
/// Unlike [`super::database_name::DatabaseName`] it carries no owner prefix and
/// is never created or granted on this host: it is only ever the hole of a remote
/// `mysqldump` command (`ops::transfer::RemoteCommand`) and the name the dump is
/// read under. The alphabet `[A-Za-z0-9_]` contains no quote, space, backtick,
/// `$`, `;` or newline, so a name that parsed cannot leave the single quotes it is
/// written inside; a source database whose name needs anything else is refused
/// here and reported by the inventory as a warning, never repaired.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct RemoteDatabaseName(String);

impl RemoteDatabaseName {
    /// Validates `candidate` and wraps it.
    ///
    /// # Errors
    ///
    /// - [`RemoteDatabaseNameError::Empty`] when it is empty.
    /// - [`RemoteDatabaseNameError::TooLong`] when it is over 64 characters.
    /// - [`RemoteDatabaseNameError::UnexpectedCharacter`] for anything outside
    ///   `[A-Za-z0-9_]`.
    pub fn parse(candidate: &str) -> Result<Self, RemoteDatabaseNameError> {
        if candidate.is_empty() {
            return Err(RemoteDatabaseNameError::Empty);
        }
        if candidate.len() > MAXIMUM_LENGTH {
            return Err(RemoteDatabaseNameError::TooLong {
                maximum: MAXIMUM_LENGTH,
            });
        }
        if !candidate
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || byte == b'_')
        {
            return Err(RemoteDatabaseNameError::UnexpectedCharacter);
        }

        Ok(Self(candidate.to_owned()))
    }

    /// The validated name.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

#[cfg(test)]
#[path = "../../tests/validation/db/remote_database_name_tests.rs"]
mod tests;
