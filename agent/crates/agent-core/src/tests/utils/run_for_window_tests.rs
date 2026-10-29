//! Tests for [`run_for_window`], where outliving the window is the success case.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::sync::mpsc;
use std::time::Duration;

use super::run_for_window;
use crate::command_outcome::CommandOutcome;
use crate::utils::child_output::ChildOutput;
use crate::utils::kill_group_and_reap::REAP_CEILING;
use crate::utils::process_probe::{
    FIND, KillOnDrop, SLEEP, as_strs, find_sleep_arguments, grandchild_pids, is_gone, leader_pids,
    unique_length, wait_until,
};
use crate::utils::spawn_error::SpawnError;
use crate::utils::window_outcome::WindowOutcome;

/// A candidate that ends inside the window returns what it printed.
#[test]
fn a_candidate_that_exits_inside_the_window_returns_what_it_printed() {
    let outcome = run_for_window(
        Path::new("/usr/bin/echo"),
        &["hi"],
        ChildOutput::DEFAULT,
        Duration::from_secs(10),
    )
    .unwrap();

    assert_eq!(
        outcome,
        WindowOutcome::Exited(CommandOutcome {
            status: 0,
            stdout: "hi\n".to_owned(),
            stderr: String::new(),
        })
    );
}

/// A candidate that is still running when the window closes is reported as
/// having outlived it, and its whole process group is gone.
#[test]
fn a_candidate_that_outlives_the_window_is_reported_outlived_and_its_whole_group_is_killed() {
    let length = unique_length(11);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };
    let window = Duration::from_secs(2);

    let (sender, receiver) = mpsc::channel();
    let worker_length = length.clone();
    std::thread::spawn(move || {
        let arguments = find_sleep_arguments(&worker_length);
        let result = run_for_window(
            Path::new(FIND),
            &as_strs(&arguments),
            ChildOutput::DEFAULT,
            window,
        );
        let _ = sender.send(result);
    });

    // Positive control: the grandchild exists while the window is open.
    assert!(
        wait_until(Duration::from_millis(1_500), || !grandchild_pids(&length)
            .is_empty()),
        "the grandchild never appeared; the stand-in did not run"
    );
    let grandchildren = grandchild_pids(&length);

    let result = receiver
        .recv_timeout(window + REAP_CEILING + Duration::from_secs(2))
        .expect("the call did not return within the window plus the reap ceiling");

    assert_eq!(result.unwrap(), WindowOutcome::Outlived);
    assert!(
        wait_until(Duration::from_secs(5), || grandchildren
            .iter()
            .all(|pid| is_gone(*pid))),
        "the grandchild survived the group kill"
    );
    assert!(leader_pids(&length).iter().all(|pid| is_gone(*pid)));
}

/// A program that cannot be started is a start error, not an outcome.
#[test]
fn a_candidate_that_cannot_start_is_a_start_error() {
    let result = run_for_window(
        Path::new("/nonexistent/maran-test-program"),
        &[],
        ChildOutput::DEFAULT,
        Duration::from_secs(1),
    );

    assert!(matches!(result, Err(SpawnError::Start { .. })));
}
