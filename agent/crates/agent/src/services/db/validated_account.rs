//! Revalidating the account name a database listing is asked about.

use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// Revalidates the account name `ListDatabases` carries.
///
/// The API validated it already. This is the agent's own check, and it exists
/// because the agent runs as root and the API does not (rules/security.md item
/// 1, which requires revalidation in the agent and not only at the API
/// boundary): the name becomes the prefix every returned database name is
/// decoded against, so a name outside the allow-list would decode rows that
/// belong to nobody.
///
/// # Errors
///
/// Returns the wire error for a name the agent will not accept — empty, too
/// long, or carrying anything outside the account allow-list.
pub fn validated_account(account_username: &str) -> Result<AccountName, AgentError> {
    AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))
}
