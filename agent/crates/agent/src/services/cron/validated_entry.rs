//! Turning an account name and an entry id into the pair three rpcs address.

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;
use crate::services::wire::validated_account::validated_account;

/// Builds the account and entry id that `DeleteCronEntry`,
/// `SetCronEntryEnabled` and `GetCronEntryOutput` all address an entry by.
///
/// One bundle for the three because they carry exactly the same two values;
/// `SetCronEntryEnabled`'s third is a boolean, which has nothing to validate
/// and no way to be malformed.
///
/// The id is revalidated rather than trusted, and that is the load-bearing part
/// of this function. The agent mints every id itself, but the one arriving here
/// came back over the wire, and it goes on to NAME three files under the
/// account's home. [`CronEntryId`]'s grammar — hex and hyphens, nothing else —
/// is what stands between that name and a path: it cannot hold a `/`, a `..`, a
/// leading separator or a NUL, so the file paths built from it cannot leave the
/// account's own cron directory.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, and
/// for an id that is not one this agent could have minted.
pub fn validated_entry(
    account_username: &str,
    entry_id: &str,
) -> Result<(AccountName, CronEntryId), AgentError> {
    let account = validated_account(account_username)?;
    let entry = CronEntryId::parse(entry_id).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, entry))
}
