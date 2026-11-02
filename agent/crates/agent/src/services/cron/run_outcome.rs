//! Turning a run started by hand into the wire's payload.

use maran_ops::cron::{CronRun, CronRunLimit as RunLimit};

use crate::proto::{CronRunLimit, RunCronEntryOk};

/// Builds the wire payload for `RunCronEntry`.
///
/// The duration saturates at `u64::MAX` milliseconds rather than failing: a
/// run is bounded at minutes, so the saturation is unreachable, and a payload
/// that could not be built would turn a finished run into a transport error.
/// "No ceiling" is the enum's zero value, which `cron.proto` documents as such.
#[must_use]
pub fn run_outcome(run: CronRun) -> RunCronEntryOk {
    let limit = match run.limit {
        None => CronRunLimit::Unspecified,
        Some(RunLimit::Runtime) => CronRunLimit::Runtime,
        Some(RunLimit::Output) => CronRunLimit::Output,
    };

    RunCronEntryOk {
        duration_ms: u64::try_from(run.duration.as_millis()).unwrap_or(u64::MAX),
        exit_code: run.exit_code,
        stdout: run.stdout,
        stderr: run.stderr,
        limit: limit as i32,
    }
}

#[cfg(test)]
#[path = "../../tests/services/cron/run_outcome_tests.rs"]
mod tests;
