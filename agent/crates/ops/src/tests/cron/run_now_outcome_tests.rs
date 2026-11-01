//! How the client's answer becomes the outcome of a run started by hand.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::cron::cron_error::CronError;
use crate::cron::model::cron_run_limit::CronRunLimit;
use crate::cron::run_now_outcome::run_now_outcome;

/// A client answer with this status and output.
fn answered(status: i32) -> Result<CommandOutcome, SpawnError> {
    Ok(CommandOutcome {
        status,
        stdout: "out".to_owned(),
        stderr: "err".to_owned(),
    })
}

/// A run that ended on its own carries its status and its output.
#[test]
fn a_run_that_ended_on_its_own_carries_its_status_and_output() {
    let run = run_now_outcome(answered(3), Duration::from_secs(2)).unwrap();

    assert_eq!(run.exit_code, Some(3));
    assert_eq!(run.stdout, "out");
    assert_eq!(run.stderr, "err");
    assert_eq!(run.limit, None);
    assert_eq!(run.duration, Duration::from_secs(2));
}

/// A run that lasted the runtime ceiling was stopped and has no status.
#[test]
fn a_run_that_lasted_the_ceiling_was_stopped_and_has_no_status() {
    let run = run_now_outcome(answered(1), Duration::from_secs(120)).unwrap();

    assert_eq!(run.exit_code, None);
    assert_eq!(run.limit, Some(CronRunLimit::Runtime));
}

/// One second under the ceiling is still a run that ended on its own.
#[test]
fn one_second_under_the_ceiling_ended_on_its_own() {
    let run = run_now_outcome(answered(0), Duration::from_secs(119)).unwrap();

    assert_eq!(run.exit_code, Some(0));
    assert_eq!(run.limit, None);
}

/// A client that died of a signal reports no status rather than a sentinel.
#[test]
fn a_client_killed_by_a_signal_reports_no_status() {
    let run = run_now_outcome(answered(-1), Duration::from_secs(1)).unwrap();

    assert_eq!(run.exit_code, None);
    assert_eq!(run.limit, None);
}

/// Output past the ceiling stops the run and keeps none of it.
#[test]
fn output_past_the_ceiling_keeps_none_of_it() {
    let answer = Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
        program: "systemd-run".to_owned(),
        stream: OutputStream::Stdout,
        cap: 65_536,
    }));

    let run = run_now_outcome(answer, Duration::from_secs(4)).unwrap();

    assert_eq!(run.limit, Some(CronRunLimit::Output));
    assert_eq!(run.exit_code, None);
    assert!(run.stdout.is_empty() && run.stderr.is_empty());
}

/// A wedged client killed at its deadline is the runtime ceiling.
#[test]
fn a_wedged_client_is_the_runtime_ceiling() {
    let answer = Err(SpawnError::Bound(BoundExceeded::TimedOut {
        program: "systemd-run".to_owned(),
        deadline: Duration::from_secs(180),
    }));

    let run = run_now_outcome(answer, Duration::from_secs(180)).unwrap();

    assert_eq!(run.limit, Some(CronRunLimit::Runtime));
}

/// A client that could not be started is no run at all.
#[test]
fn a_client_that_could_not_start_is_unavailable() {
    let answer = Err(SpawnError::Start {
        program: "systemd-run".to_owned(),
        reason: "missing".to_owned(),
    });

    assert_eq!(
        run_now_outcome(answer, Duration::ZERO),
        Err(CronError::RunUnavailable)
    );
}
