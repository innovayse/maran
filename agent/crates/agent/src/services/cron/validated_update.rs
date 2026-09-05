//! Turning an `UpdateCronEntry` request into the values the operation takes.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::proto::CronSchedule as WireSchedule;
use crate::services::cron::validated_entry::validated_entry;
use crate::services::cron::validated_schedule::validated_schedule;
use crate::services::wire::invalid_input::invalid_input;

/// Builds the four values `UpdateCronEntry` needs from what it carries.
///
/// The request's `enabled` field is NOT among them, and its absence here is the
/// point: an update rewrites what an entry runs and leaves its enablement
/// exactly as it was. Reading the boolean would make "change this command"
/// silently re-enable an entry the operator had switched off, because proto3's
/// default for a boolean nobody set is `false` — the field is deprecated in
/// `cron.proto` and `SetCronEntryEnabled` is the rpc that changes that state.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for an
/// entry id that is not one this agent could have minted, for an absent or
/// malformed schedule, and for a command carrying a control character or
/// exceeding the size a single command line may have.
pub fn validated_update(
    account_username: &str,
    entry_id: &str,
    schedule: Option<&WireSchedule>,
    command: &str,
) -> Result<(AccountName, CronEntryId, CronSchedule, CronCommand), AgentError> {
    let (account, entry) = validated_entry(account_username, entry_id)?;
    let schedule = validated_schedule(schedule)?;
    let command = CronCommand::parse(command).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, entry, schedule, command))
}
