//! Turning a `CreateDatabaseUser` request into the typed input the operation takes.

use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::db::DatabaseUserRequest;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Builds the login and the password `CreateDatabaseUser` creates it with.
///
/// The login is REBUILT from the account (`DbUserName` has no constructor that
/// takes a whole name), so a request naming another tenant's login produces a name
/// under the caller's own account. The password cannot hold a quote, a backslash or
/// a backtick, so it cannot break out of the `IDENTIFIED BY '<value>'` literal the
/// operation interpolates it into. Both are types and not checks, for the reason
/// `validated_creation` sets out.
///
/// # Errors
///
/// Returns the wire error for a suffix that is empty, carries anything outside
/// `[a-z0-9]` or makes a name past the server's thirty-two byte user-name limit,
/// and for a password outside the allowed alphabet. The message names the
/// condition and never the value.
pub fn validated_user_creation(
    account: &AccountName,
    db_username: &str,
    password: &str,
) -> Result<DatabaseUserRequest, AgentError> {
    let user = DbUserName::for_account(account, db_username)
        .map_err(|error| invalid_input(error.to_string()))?;
    let password = Password::parse(password).map_err(|error| invalid_input(error.to_string()))?;

    Ok(DatabaseUserRequest { user, password })
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_user_creation_tests.rs"]
mod tests;
