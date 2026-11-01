//! How the answer of `systemd-run` becomes the outcome of a run.

use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::cron::cron_error::CronError;
use crate::cron::model::cron_run::CronRun;
use crate::cron::model::cron_run_limit::CronRunLimit;
use crate::cron::run_now_arguments::RUN_NOW_RUNTIME_CEILING;

/// Reads what starting an entry by hand produced into a [`CronRun`].
///
/// Four answers, and each says only what was observed:
///
/// - **The client returned before the runtime ceiling.** The job ended on its
///   own; `systemd-run --wait` exits with the job's status, so that status is
///   the run's. A negative status is the bounded core's "died of a signal it did
///   not send", which is no exit status at all and is reported as none.
/// - **The client returned at or after the runtime ceiling.** The service
///   manager stopped the unit, so the run has no status of its own and is
///   reported as stopped by [`CronRunLimit::Runtime`]. A job that finished in
///   exactly that second reads the same way; the threat note states it.
/// - **An output stream passed its ceiling.** The core killed the client and
///   kept nothing, so the run is [`CronRunLimit::Output`] with no output: a
///   prefix would read as the whole.
/// - **The client outlived its own deadline.** It was wedged and was killed; the
///   unit's `RuntimeMaxSec` still ends the job, so this is the runtime ceiling
///   too.
///
/// # Errors
///
/// Returns [`CronError::RunUnavailable`] when the client could not be started
/// or waited for, so nothing is known about the run.
pub(crate) fn run_now_outcome(
    answer: Result<CommandOutcome, SpawnError>,
    elapsed: Duration,
) -> Result<CronRun, CronError> {
    match answer {
        Ok(outcome) if elapsed >= RUN_NOW_RUNTIME_CEILING => Ok(CronRun {
            duration: elapsed,
            exit_code: None,
            stdout: outcome.stdout,
            stderr: outcome.stderr,
            limit: Some(CronRunLimit::Runtime),
        }),
        Ok(outcome) => Ok(CronRun {
            duration: elapsed,
            exit_code: (outcome.status >= 0).then_some(outcome.status),
            stdout: outcome.stdout,
            stderr: outcome.stderr,
            limit: None,
        }),
        Err(SpawnError::Bound(BoundExceeded::OutputTooLarge { .. })) => {
            Ok(stopped(elapsed, CronRunLimit::Output))
        }
        Err(SpawnError::Bound(BoundExceeded::TimedOut { .. })) => {
            Ok(stopped(elapsed, CronRunLimit::Runtime))
        }
        Err(_) => Err(CronError::RunUnavailable),
    }
}

/// A run a ceiling stopped, carrying no status and no output.
fn stopped(elapsed: Duration, limit: CronRunLimit) -> CronRun {
    CronRun {
        duration: elapsed,
        exit_code: None,
        stdout: String::new(),
        stderr: String::new(),
        limit: Some(limit),
    }
}

#[cfg(test)]
#[path = "../tests/cron/run_now_outcome_tests.rs"]
mod tests;
