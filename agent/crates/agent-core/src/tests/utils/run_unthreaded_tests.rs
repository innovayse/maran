//! Tests for [`run_unthreaded`], the bounded core for a forked child.
//!
//! Whether a thread was started cannot be observed from inside a test binary
//! that runs many tests on many threads, so that property is checked
//! structurally by reading the source (the architecture test below).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Write as _;
use std::path::Path;
use std::process::Stdio;
use std::sync::mpsc;
use std::time::Duration;

use super::run_unthreaded;
use crate::utils::bound_exceeded::BoundExceeded;
use crate::utils::kill_group_and_reap::REAP_CEILING;
use crate::utils::process_probe::{
    FIND, KillOnDrop, SLEEP, as_strs, find_sleep_arguments, grandchild_pids, is_gone, leader_pids,
    run_inner_half, unique_length, wait_until,
};
use crate::utils::spawn_error::SpawnError;

/// Set in the inner half of the environment witness.
const ENVIRONMENT_MARKER: &str = "MARAN_SPAWN_UNTHREADED_ENVIRONMENT_INNER";

/// The full test path of the environment witness.
const ENVIRONMENT_CASE: &str = "utils::run_unthreaded::tests::the_unthreaded_core_gives_its_child_the_declared_environment_only";

/// At the deadline the whole group is killed and the typed timeout returned.
#[test]
fn the_unthreaded_core_kills_the_group_at_the_deadline() {
    let length = unique_length(21);
    let _cleanup = KillOnDrop {
        argv: vec![SLEEP.to_owned(), length.clone()],
    };
    let deadline = Duration::from_secs(2);

    let (sender, receiver) = mpsc::channel();
    let worker_length = length.clone();
    std::thread::spawn(move || {
        let arguments = find_sleep_arguments(&worker_length);
        let result = run_unthreaded(
            Path::new(FIND),
            &as_strs(&arguments),
            Stdio::null(),
            deadline,
        );
        let _ = sender.send(result);
    });

    // Positive control: the grandchild exists while the call runs.
    assert!(
        wait_until(Duration::from_millis(1_500), || !grandchild_pids(&length)
            .is_empty()),
        "the grandchild never appeared; the stand-in did not run"
    );
    let grandchildren = grandchild_pids(&length);

    let result = receiver
        .recv_timeout(deadline + REAP_CEILING + Duration::from_secs(2))
        .expect("the call did not return within the deadline plus the reap ceiling");

    match result {
        Err(SpawnError::Bound(BoundExceeded::TimedOut {
            program,
            deadline: reported,
        })) => {
            assert_eq!(program, FIND);
            assert_eq!(reported, deadline);
        }
        other => panic!("expected a timeout, got {other:?}"),
    }
    assert!(
        wait_until(Duration::from_secs(5), || grandchildren
            .iter()
            .all(|pid| is_gone(*pid))),
        "the grandchild survived the group kill"
    );
    assert!(leader_pids(&length).is_empty(), "the leader was not reaped");
}

/// The exit code comes back as the result.
#[test]
fn the_unthreaded_core_returns_the_exit_code() {
    let failed = run_unthreaded(
        Path::new("/usr/bin/false"),
        &[],
        Stdio::null(),
        Duration::from_secs(30),
    )
    .unwrap();
    let succeeded = run_unthreaded(
        Path::new("/usr/bin/true"),
        &[],
        Stdio::null(),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(failed, 1);
    assert_eq!(succeeded, 0);
}

/// A program that cannot be started is a start error.
#[test]
fn the_unthreaded_core_reports_a_program_that_cannot_start() {
    let result = run_unthreaded(
        Path::new("/nonexistent/maran-test-program"),
        &[],
        Stdio::null(),
        Duration::from_secs(1),
    );

    assert!(matches!(result, Err(SpawnError::Start { .. })));
}

/// The caller's standard input reaches the child, and a different input is
/// told apart (the inverse control).
#[test]
fn the_unthreaded_core_delivers_the_callers_stdin() {
    let mut same = tempfile::NamedTempFile::new().unwrap();
    same.write_all(b"the artifact\n").unwrap();
    same.flush().unwrap();
    let mut other = tempfile::NamedTempFile::new().unwrap();
    other.write_all(b"another file\n").unwrap();
    other.flush().unwrap();

    let same_path = same.path().display().to_string();
    let matching = run_unthreaded(
        Path::new("/usr/bin/cmp"),
        &["-", same_path.as_str()],
        Stdio::from(std::fs::File::open(same.path()).unwrap()),
        Duration::from_secs(30),
    )
    .unwrap();
    let differing = run_unthreaded(
        Path::new("/usr/bin/cmp"),
        &["-", same_path.as_str()],
        Stdio::from(std::fs::File::open(other.path()).unwrap()),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(matching, 0, "the child did not read the caller's input");
    assert_eq!(differing, 1, "the comparison cannot tell inputs apart");
}

/// The child gets the declared environment only.
///
/// The outer half plants `LD_PRELOAD` in a copy of this test binary; the inner
/// half asks `printenv` for it through the core and must get exit 1 (not set).
/// Positive control: `printenv PATH` exits 0, so the probe can see a variable
/// that IS declared.
#[test]
fn the_unthreaded_core_gives_its_child_the_declared_environment_only() {
    if std::env::var(ENVIRONMENT_MARKER).is_err() {
        run_inner_half(
            ENVIRONMENT_CASE,
            ENVIRONMENT_MARKER,
            "1",
            Stdio::null(),
            &[("LD_PRELOAD", "/nonexistent/maran-test-payload.so")],
        );
        return;
    }

    let poisoned = run_unthreaded(
        Path::new("/usr/bin/printenv"),
        &["LD_PRELOAD"],
        Stdio::null(),
        Duration::from_secs(30),
    )
    .unwrap();
    let declared = run_unthreaded(
        Path::new("/usr/bin/printenv"),
        &["PATH"],
        Stdio::null(),
        Duration::from_secs(30),
    )
    .unwrap();

    assert_eq!(poisoned, 1, "LD_PRELOAD reached the child");
    assert_eq!(declared, 0, "the probe cannot see a declared variable");
}

/// The patterns that start a thread, in the form the standard library spells them.
const THREAD_STARTERS: [&str; 4] = [
    "thread::spawn",
    "thread::Builder",
    "thread::scope",
    "mpsc::",
];

/// Whether the code lines of `source` (comments dropped) start a thread.
fn starts_a_thread(source: &str) -> bool {
    source
        .lines()
        .filter(|line| !line.trim_start().starts_with("//"))
        .any(|line| THREAD_STARTERS.iter().any(|pattern| line.contains(pattern)))
}

/// The unthreaded core starts no thread, nor does any part it calls.
///
/// An ARCHITECTURE test, because a thread count taken inside a multi-threaded
/// test binary is noise: this property can only be checked structurally. The
/// positive control is the same scan finding a thread in the reader the
/// threaded core uses.
#[test]
fn the_unthreaded_core_starts_no_thread() {
    let reader = include_str!("../../utils/start_reader.rs");
    assert!(
        starts_a_thread(reader),
        "the scan cannot see a thread start; it would pass vacuously"
    );

    let parts = [
        (
            "run_unthreaded",
            include_str!("../../utils/run_unthreaded.rs"),
        ),
        ("has_exited", include_str!("../../utils/has_exited.rs")),
        (
            "kill_group_and_reap",
            include_str!("../../utils/kill_group_and_reap.rs"),
        ),
        ("launch_child", include_str!("../../utils/launch_child.rs")),
        (
            "poll_interval",
            include_str!("../../utils/poll_interval.rs"),
        ),
        ("deadline", include_str!("../../utils/deadline.rs")),
    ];
    for (name, source) in parts {
        assert!(!starts_a_thread(source), "{name} starts a thread");
    }
}
