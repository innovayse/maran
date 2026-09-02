//! Rebuilding the database AND the user a `DropDatabase` takes away.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;

use crate::proto::AgentError;
use crate::services::db::validated_account::validated_account;
use crate::services::sites::invalid_input::invalid_input;

/// Rebuilds the database and the dedicated user a `DropDatabase` removes.
///
/// Both halves are rebuilt from the account for the reason
/// [`validated_database`](crate::services::db::validated_database::validated_database)
/// sets out: neither type has a constructor that takes a whole name, so a
/// request cannot name another tenant's database or another tenant's user.
///
/// They are two suffixes rather than one because the customer names them
/// independently (`db.proto`), so there is nothing to derive either from the
/// other with. A drop that guessed the user from the database would either
/// leave a live credential behind on the server or remove one belonging to a
/// different database of the same account.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, or for
/// either suffix being empty, carrying anything outside `[a-z0-9]`, or
/// producing a name past the server's sixty-four byte identifier limit.
pub fn validated_removal(
    account_username: &str,
    database_name: &str,
    db_username: &str,
) -> Result<(DatabaseName, DbUserName), AgentError> {
    let account = validated_account(account_username)?;

    let database = DatabaseName::for_account(&account, database_name)
        .map_err(|error| invalid_input(error.to_string()))?;
    let user = DbUserName::for_account(&account, db_username)
        .map_err(|error| invalid_input(error.to_string()))?;

    Ok((database, user))
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_removal_tests.rs"]
mod tests;
