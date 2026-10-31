//! Revalidating the account names a disk-usage request carries.

use maran_ops::monitor::AccountSelection;

use crate::proto::AgentError;
use crate::services::wire::validated_account::validated_account;

/// Builds the selection a `GetAccountsDiskUsage` request asks for.
///
/// **An empty list is every hosting account**, because that is what the field
/// meant before it existed: a proto3 `repeated` field a caller did not set
/// arrives empty, and an older panel that never sets it must keep its answer.
/// A non-empty list is exactly those accounts, and the reading then walks no
/// other home.
///
/// Every name goes through the agent's own account check, and one bad name
/// refuses the whole request rather than being dropped. A dropped name would
/// make an account vanish from the answer the same way an account that does
/// not exist does, and the panel could not tell a refused input from a deleted
/// account.
///
/// # Errors
///
/// Returns the invalid-input wire error for any name the agent will not accept
/// as an account name — empty, too long, or carrying anything outside the
/// account allow-list.
pub fn validated_account_selection(
    account_usernames: &[String],
) -> Result<AccountSelection, AgentError> {
    if account_usernames.is_empty() {
        return Ok(AccountSelection::Every);
    }

    account_usernames
        .iter()
        .map(|name| validated_account(name))
        .collect::<Result<_, _>>()
        .map(AccountSelection::Only)
}

#[cfg(test)]
#[path = "../../tests/services/monitor/validated_account_selection_tests.rs"]
mod tests;
