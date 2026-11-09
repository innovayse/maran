//! Rebuilding the login a `DropDatabaseUser` takes away.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Rebuilds the login a `DropDatabaseUser` removes, under the account the panel
/// authorised.
///
/// `DbUserName` has no constructor that takes a whole name, so a request cannot
/// name another tenant's login: a suffix is all it can express, and the prefix is
/// the caller's own.
///
/// # Errors
///
/// Returns the wire error for a suffix that is empty, carries anything outside
/// `[a-z0-9]` or makes a name past the server's thirty-two byte user-name limit.
pub fn validated_user_removal(
    account: &AccountName,
    db_username: &str,
) -> Result<DbUserName, AgentError> {
    DbUserName::for_account(account, db_username).map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_user_removal_tests.rs"]
mod tests;
