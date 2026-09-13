//! Revalidating the account name an rpc carries.

use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the account name an rpc carries.
///
/// The API validated it already. This is the agent's own check, and it exists
/// because the agent runs as root and the API does not (rules/security.md item
/// 1, which requires revalidation in the agent and not only at the API
/// boundary). What the name goes on to decide differs per service — whose
/// crontab is edited, which home is written under, which prefix database rows
/// are decoded against — but every one of those is an argument handed to a
/// root process, which is why the gate is shared and unconditional.
///
/// # Errors
///
/// Returns the wire error for a name the agent will not accept — empty, too
/// long, or carrying anything outside the account allow-list.
pub fn validated_account(account_username: &str) -> Result<AccountName, AgentError> {
    AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))
}
