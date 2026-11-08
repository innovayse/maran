//! Turning a `SetDatabaseUserPrivileges` request into the typed input the operation takes.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_ops::db::DatabasePrivilegeChange;

use crate::proto::AgentError;
use crate::services::db::validated_privilege_set::validated_privilege_set;
use crate::services::wire::invalid_input::invalid_input;

/// Builds the login, the database and the privilege set a change applies to.
///
/// The login and the database are both rebuilt from the account, so a request
/// cannot name another tenant's login or database; the two suffixes are separate
/// because the customer names them independently (`db.proto`). The privileges go
/// through [`validated_privilege_set`], the only place the wire's numbers become
/// privileges.
///
/// # Errors
///
/// Returns the wire error for either suffix being empty, carrying anything outside
/// `[a-z0-9]` or making a name past the server's limit, and for any refusal of
/// [`validated_privilege_set`].
pub fn validated_privilege_change(
    account: &AccountName,
    db_username: &str,
    database_name: &str,
    privileges: &[i32],
) -> Result<DatabasePrivilegeChange, AgentError> {
    let user = DbUserName::for_account(account, db_username)
        .map_err(|error| invalid_input(error.to_string()))?;
    let database = DatabaseName::for_account(account, database_name)
        .map_err(|error| invalid_input(error.to_string()))?;
    let privileges = validated_privilege_set(privileges)?;

    Ok(DatabasePrivilegeChange {
        user,
        database,
        privileges,
    })
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_privilege_change_tests.rs"]
mod tests;
