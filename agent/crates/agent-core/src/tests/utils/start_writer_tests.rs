//! Tests for [`start_writer`]: input reaches the child and the pipe is closed
//! after it, and a write that fails is reported.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Read as _;
use std::path::Path;
use std::process::Stdio;
use std::sync::mpsc;
use std::time::Duration;

use super::start_writer;
use crate::utils::child_event::ChildEvent;
use crate::utils::launch_child::launch_child;

/// How long a case waits for something that must already be happening.
const PATIENCE: Duration = Duration::from_secs(10);

/// Bytes written to a program that reads nothing: far more than a pipe buffer
/// holds, so the write cannot finish before the child's end closes.
const MORE_THAN_A_PIPE_HOLDS: usize = 1 << 20;

/// A child that exits without reading leaves the write with nowhere to go, and
/// the writer reports `InputFailed` instead of ending silently.
#[test]
fn a_write_that_cannot_complete_is_reported_as_an_input_failure() {
    let mut child = launch_child(
        Path::new("/usr/bin/true"),
        &[],
        Stdio::piped(),
        Stdio::null(),
        Stdio::null(),
    )
    .unwrap();
    let pipe = child.stdin.take().unwrap();
    let (sender, events) = mpsc::channel::<ChildEvent>();

    start_writer(pipe, vec![b'x'; MORE_THAN_A_PIPE_HOLDS], sender).unwrap();

    let event = events.recv_timeout(PATIENCE);
    let _ = child.wait();

    assert!(
        matches!(event, Ok(ChildEvent::InputFailed)),
        "expected an input failure, got {event:?}"
    );
}

/// Inverse control: a write that completes reports delivery, the child receives
/// every byte, and the pipe is closed afterwards (`cat` only ends at
/// end-of-input, so its ending is the proof).
#[test]
fn a_completed_write_reports_delivery_and_closes_the_pipe() {
    let mut child = launch_child(
        Path::new("/usr/bin/cat"),
        &[],
        Stdio::piped(),
        Stdio::piped(),
        Stdio::null(),
    )
    .unwrap();
    let pipe = child.stdin.take().unwrap();
    let mut stdout = child.stdout.take().unwrap();
    let (sender, events) = mpsc::channel::<ChildEvent>();

    // Read on a thread so a pipe left open fails this case by name instead of
    // hanging the suite.
    let (read_sender, read_events) = mpsc::channel::<String>();
    std::thread::spawn(move || {
        let mut text = String::new();
        let _ = stdout.read_to_string(&mut text);
        let _ = read_sender.send(text);
    });

    start_writer(pipe, b"hello writer".to_vec(), sender).unwrap();

    let echoed = read_events.recv_timeout(PATIENCE);
    let reported = events.recv_timeout(PATIENCE);
    if echoed.is_err() {
        let _ = child.kill();
    }
    let _ = child.wait();

    assert_eq!(
        echoed.as_deref(),
        Ok("hello writer"),
        "the child did not receive the bytes, or the pipe was left open"
    );
    assert!(
        matches!(reported, Ok(ChildEvent::InputDelivered)),
        "a successful write must report delivery; got {reported:?}"
    );
}
