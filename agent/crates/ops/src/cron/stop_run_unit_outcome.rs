//! How the answer of `systemctl stop` becomes the outcome of stopping a run.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::cron::cron_error::CronError;

/// `systemctl`'s exit status for a unit it does not have loaded.
///
/// The LSB "program is not installed" status. For a transient unit started with
/// `--collect` it means the job ended by itself between the moment the ceiling
/// was reached and the moment the stop ran, which is the outcome being asked
/// for.
const UNIT_NOT_LOADED: i32 = 5;

/// Reads what `systemctl stop` of a run's unit produced.
///
/// Zero is a unit that was stopped; [`UNIT_NOT_LOADED`] is a unit that was
/// already gone. Both are the job not running, which is all that is asked. The
/// stop is waited for by the caller (the `Reload` class's deadline exists for
/// the service manager's stop-then-kill sequence), because "stop requested" is
/// not "stopped".
///
/// # Errors
///
/// [`CronError::RunNotStopped`] when `systemctl` could not be started, outran
/// its deadline, or refused for any reason other than the unit already being
/// gone. The job may then still be running until its runtime ceiling.
pub(crate) fn stop_run_unit_outcome(
    answer: Result<CommandOutcome, SpawnError>,
) -> Result<(), CronError> {
    match answer.map_err(|_| CronError::RunNotStopped)?.status {
        0 | UNIT_NOT_LOADED => Ok(()),
        _ => Err(CronError::RunNotStopped),
    }
}

#[cfg(test)]
#[path = "../tests/cron/stop_run_unit_outcome_tests.rs"]
mod tests;
