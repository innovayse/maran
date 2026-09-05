//! Rebuilding one database's name from the account the panel authorised.

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Rebuilds the database named by `account_username` and the suffix
/// `database_name`.
///
/// **The name is built, never forwarded.** `DatabaseName` has no constructor
/// that takes a whole name: the only way to obtain one is `for_account`, which
/// applies the account prefix and restricts the suffix to `[a-z0-9]`. So a
/// request cannot name another tenant's database — a suffix that tried would
/// produce a name under the CALLER's own account, which is a name the caller is
/// entitled to. That is the tenant boundary for this area, and it is enforced
/// by a type rather than by a check a handler could forget: the server itself
/// has no notion of a tenant and would drop or measure whatever it was given.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for an
/// empty suffix, for one carrying anything outside `[a-z0-9]` — the separator
/// included, so a suffix cannot smuggle in a second prefix — or for a prefixed
/// result past the server's sixty-four byte identifier limit.
pub fn validated_database(
    account_username: &str,
    database_name: &str,
) -> Result<DatabaseName, AgentError> {
    let account = validated_account(account_username)?;

    DatabaseName::for_account(&account, database_name)
        .map_err(|error| invalid_input(error.to_string()))
}
