//! Turning a `CreateCronEntry` request into the values the operation takes.

use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;

use crate::proto::AgentError;
use crate::proto::CronSchedule as WireSchedule;
use crate::services::cron::validated_account::validated_account;
use crate::services::cron::validated_schedule::validated_schedule;
use crate::services::sites::invalid_input::invalid_input;

/// Builds the three values `CreateCronEntry` needs from what it carries.
///
/// Every one of them is a validated type and none is a `String`. That is this
/// area's injection defence: a crontab is line-oriented, so a newline anywhere
/// in a value the agent writes would let one entry inject further entries,
/// schedules or environment assignments into the account's table
/// (rules/security.md §4). The schedule is five checked fields rather than a
/// checked line, and the command cannot hold a control character at all.
///
/// The command's alphabet is deliberately WIDE otherwise: `%` and `#` are
/// ordinary characters here, because the command never reaches the crontab —
/// it is written verbatim to a file the installed line names. Refusing them
/// would refuse `date +%s` and a trailing comment for a hazard that does not
/// exist in this design.
///
/// # Errors
///
/// Returns the wire error for an account name the agent will not accept, for an
/// absent or malformed schedule, and for a command carrying a control character
/// or exceeding the size a single command line may have.
pub fn validated_creation(
    account_username: &str,
    schedule: Option<&WireSchedule>,
    command: &str,
) -> Result<(AccountName, CronSchedule, CronCommand), AgentError> {
    let account = validated_account(account_username)?;
    let schedule = validated_schedule(schedule)?;
    let command = CronCommand::parse(command).map_err(|error| invalid_input(error.to_string()))?;

    Ok((account, schedule, command))
}
