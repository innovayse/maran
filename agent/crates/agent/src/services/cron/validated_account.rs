//! Revalidating the account name a cron request names.

use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// Revalidates the account name every cron rpc carries.
///
/// The API validated it already. This is the agent's own check, and it exists
/// because the agent runs as root and the API does not (rules/security.md item
/// 1, which requires revalidation in the agent and not only at the API
/// boundary). Here the name decides two things at once: whose crontab
/// `crontab -u` is asked for, and which home the entry's command, output and
/// exit files are written under. A name outside the allow-list would reach both
/// as a bare argument.
///
/// # Errors
///
/// Returns the wire error for a name the agent will not accept — empty, too
/// long, or carrying anything outside the account allow-list.
pub fn validated_account(account_username: &str) -> Result<AccountName, AgentError> {
    AccountName::parse(account_username).map_err(|error| invalid_input(error.to_string()))
}
