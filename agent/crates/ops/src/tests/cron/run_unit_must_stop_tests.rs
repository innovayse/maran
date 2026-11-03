//! Which answers of the client leave a unit for the agent to stop.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;
use maran_agent_core::utils::spawn_error::SpawnError;

use super::run_unit_must_stop;

#[test]
fn a_client_killed_for_its_output_leaves_a_unit_to_stop() {
    let answer = Err(SpawnError::Bound(BoundExceeded::OutputTooLarge {
        program: "systemd-run".to_owned(),
        stream: OutputStream::Stdout,
        cap: 65_536,
    }));

    assert!(run_unit_must_stop(&answer));
}

#[test]
fn a_client_killed_at_its_deadline_leaves_a_unit_to_stop() {
    let answer = Err(SpawnError::Bound(BoundExceeded::TimedOut {
        program: "systemd-run".to_owned(),
        deadline: Duration::from_secs(180),
    }));

    assert!(run_unit_must_stop(&answer));
}

#[test]
fn a_client_that_returned_has_waited_for_its_job_and_leaves_nothing_to_stop() {
    // The inverse control: a predicate that was true for everything would stop
    // the unit of every run, including one that finished by itself.
    let answer = Ok(CommandOutcome {
        status: 0,
        stdout: String::new(),
        stderr: String::new(),
    });

    assert!(!run_unit_must_stop(&answer));
}
