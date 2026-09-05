//! Turning the schedule message into the five validated fields cron wants.

use maran_agent_core::validation::system::cron_schedule::CronSchedule;

use crate::proto::AgentError;
use crate::proto::CronSchedule as WireSchedule;
use crate::services::wire::invalid_input::invalid_input;

/// The refusal for a request whose schedule message is absent altogether.
///
/// A named constant rather than a sentence typed twice: creation and update
/// both refuse the same way, and one wording keeps the panel's own mapping of
/// this refusal from depending on which rpc produced it.
const MISSING_SCHEDULE: &str = "a cron entry needs a schedule";

/// Validates the five fields of the wire's schedule message.
///
/// A file of its own because both `CreateCronEntry` and `UpdateCronEntry` need
/// it and because the absent-message case is a real decision: a proto3 message
/// field is optional on the wire, so a request that names no schedule at all
/// arrives as `None`. It is REFUSED rather than defaulted — the obvious default
/// would be five `*`s, which is a job that runs every minute of every day, and
/// nothing about a caller forgetting a field says that is what they meant.
///
/// # Errors
///
/// Returns the wire error when the schedule message is absent, and when any of
/// the five fields is not conventional cron syntax for its position — an
/// unknown shape, a padded number, a value outside the field's bounds, a
/// backwards range, or a zero or oversized step. The message names the
/// condition, never a path or a value the agent went on to use.
pub fn validated_schedule(schedule: Option<&WireSchedule>) -> Result<CronSchedule, AgentError> {
    let schedule = schedule.ok_or_else(|| invalid_input(MISSING_SCHEDULE.to_owned()))?;

    CronSchedule::parse(
        &schedule.minute,
        &schedule.hour,
        &schedule.day_of_month,
        &schedule.month,
        &schedule.day_of_week,
    )
    .map_err(|error| invalid_input(error.to_string()))
}
