//! What a stopped, an already-gone, a refused and an unstartable stop mean for a run.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::spawn_error::SpawnError;

use super::stop_run_unit_outcome;
use crate::cron::cron_error::CronError;

/// A finished `systemctl` with `status` and nothing printed.
fn exited_with(status: i32) -> Result<CommandOutcome, SpawnError> {
    Ok(CommandOutcome {
        status,
        stdout: String::new(),
        stderr: String::new(),
    })
}

#[test]
fn a_unit_that_was_stopped_is_a_run_that_is_no_longer_going() {
    assert_eq!(stop_run_unit_outcome(exited_with(0)), Ok(()));
}

#[test]
fn a_unit_that_was_already_gone_is_a_run_that_is_no_longer_going() {
    // `--collect` removes a transient unit the moment its job ends, so a job
    // that finished between the ceiling and the stop is not loaded any more.
    assert_eq!(stop_run_unit_outcome(exited_with(5)), Ok(()));
}

#[test]
fn any_other_status_is_a_run_that_could_not_be_confirmed_stopped() {
    // The inverse control of the two above: a reading that accepted every
    // status would let a refused stop be reported as a stopped run.
    for status in [1, 2, 3, 4, 6, 126, 127, 255, -1] {
        assert_eq!(
            stop_run_unit_outcome(exited_with(status)),
            Err(CronError::RunNotStopped),
            "status {status}"
        );
    }
}

#[test]
fn a_stop_that_could_not_be_started_is_a_run_that_could_not_be_confirmed_stopped() {
    let answer = Err(SpawnError::Start {
        program: "/usr/bin/systemctl".to_owned(),
        reason: "no such file".to_owned(),
    });
    assert_eq!(stop_run_unit_outcome(answer), Err(CronError::RunNotStopped));
}
