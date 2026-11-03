//! Stopping the transient unit a run by hand started.

use maran_agent_core::end_of_options::END_OF_OPTIONS;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::spawn_argv::spawn_argv;
use maran_distro::AdapterBinary;

use crate::cron::cron_error::CronError;
use crate::cron::model::run_unit_name::RunUnitName;

/// `systemctl`'s verb for ending a unit.
const STOP: &str = "stop";

/// `systemctl`'s exit status for a unit it does not have loaded.
///
/// The LSB "program is not installed" status. For a transient unit started with
/// `--collect` it means the job ended by itself between the moment the ceiling
/// was reached and the moment this ran, which is the outcome being asked for.
const UNIT_NOT_LOADED: i32 = 5;

/// Stops `unit` through the service manager and waits until it is down
/// (issue #336).
///
/// Called when a ceiling ended a run by hand: the output ceiling kills the
/// `systemd-run` CLIENT, and the client's death does not end the job, which the
/// service manager keeps running until its own `RuntimeMaxSec`. The panel tells
/// the customer the run was stopped, so the agent has to make that true.
///
/// **The argument vector is the verb, the end-of-options marker and the unit's
/// full name, and nothing else.** The name is a [`RunUnitName`], built by the
/// agent from the account's uid, a validated entry id and a counter, so no
/// request text reaches it; the program is the distro adapter's
/// `service_manager`, a type only the `distro` crate can construct. No shell is
/// involved (rules/security.md items 3 and 12).
///
/// **Waits** for the stop, because "stop requested" is not "stopped": the unit's
/// `TimeoutStopSec` is set to a few seconds when it is started
/// (`run_now_arguments`), so a job that ignores `SIGTERM` is killed within that
/// time and this call returns after it. The deadline is the `Reload` class's,
/// which exists for exactly the stop-then-kill sequence of the service manager.
///
/// # Errors
///
/// [`CronError::RunNotStopped`] when `systemctl` could not be started, outran
/// its deadline, or refused for any reason other than the unit already being
/// gone. The job may then still be running until its runtime ceiling.
pub(crate) fn stop_run_unit(
    service_manager: &AdapterBinary,
    unit: &RunUnitName,
) -> Result<(), CronError> {
    let service = unit.service();
    let outcome = spawn_argv(
        service_manager.as_path(),
        &[STOP, END_OF_OPTIONS, &service],
        CallClass::Reload,
    )
    .map_err(|_| CronError::RunNotStopped)?;

    reading_of(outcome.status)
}

/// What `systemctl stop`'s exit status means for a run being ended.
///
/// Zero is a unit that was stopped; [`UNIT_NOT_LOADED`] is a unit that was
/// already gone. Both are the job not running, which is all that is asked.
fn reading_of(status: i32) -> Result<(), CronError> {
    match status {
        0 | UNIT_NOT_LOADED => Ok(()),
        _ => Err(CronError::RunNotStopped),
    }
}

#[cfg(test)]
#[path = "../tests/cron/stop_run_unit_tests.rs"]
mod tests;
