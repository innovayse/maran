//! Turning a `CreateDatabase` request into the typed input the operation takes.

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_ops::db::CreateDatabaseRequest;

use crate::proto::AgentError;
use crate::services::db::validated_account::validated_account;
use crate::services::sites::invalid_input::invalid_input;

/// Builds the operation's input from the four values `CreateDatabase` carries.
///
/// Every field of the result is a validated type and none of them is a
/// `String`, which is the whole injection defence of the database area: the
/// server's DDL cannot parameterise an identifier or the literal in `IDENTIFIED
/// BY`, so the operation interpolates all three, and what makes that safe is
/// that none of these types can hold a quote, a backtick, a backslash, a
/// semicolon, a space or a newline. The values are validated, not escaped
/// (rules/rust.md "Validation first"), and this function is the one place the
/// wire's strings become them.
///
/// The password is parsed here and moved once. It is never logged, never put in
/// an error, and never echoed back: `Password` prints itself as `<password>`,
/// so even a `Debug` of the result cannot leak it.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for
/// either suffix being empty or outside `[a-z0-9]`, for a prefixed name past
/// the server's sixty-four byte identifier limit, or for a password outside the
/// allowed alphabet — which is the check that keeps a quote out of `IDENTIFIED
/// BY '…'`. The message names the condition and never the value.
pub fn validated_creation(
    account_username: &str,
    database_name: &str,
    db_username: &str,
    password: &str,
) -> Result<CreateDatabaseRequest, AgentError> {
    let account = validated_account(account_username)?;

    let database = DatabaseName::for_account(&account, database_name)
        .map_err(|error| invalid_input(error.to_string()))?;
    let user = DbUserName::for_account(&account, db_username)
        .map_err(|error| invalid_input(error.to_string()))?;
    let password = Password::parse(password).map_err(|error| invalid_input(error.to_string()))?;

    Ok(CreateDatabaseRequest {
        database,
        user,
        password,
    })
}

#[cfg(test)]
#[path = "../../tests/services/db/validated_creation_tests.rs"]
mod tests;
