//! The byte channel: what comes back from the child, what is refused, and what is cleaned up.
//!
//! A test is not root and cannot drop to another account, so these run through the private seam
//! with a drop step that succeeds or fails on demand. The real drop is exercised by
//! `crates/agent/tests/privileges_on_a_real_host.rs` in the polygon; what runs here is everything
//! AROUND it that the channel adds. Every refusal is paired with the acceptance that sits next to
//! it, so a function that refused everything, or accepted everything, cannot pass both.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::Write;
use std::time::{Duration, Instant};

use super::fork_for_bytes;
use crate::privs::child_exit_status::EXIT_DROP_SETUID_FAILED;
use crate::privs::priv_error::PrivError;

/// The patience of a test whose child is expected to finish: long enough for a loaded runner, so a
/// slow machine cannot turn an acceptance into a timeout.
const PATIENCE: Duration = Duration::from_secs(30);

/// A drop step that succeeds: the test is not root, and the drop is not what is under test.
fn no_drop() -> Result<(), i32> {
    Ok(())
}

/// A recognisable, non-repeating-looking pattern of `length` bytes, so a result that arrives
/// shuffled, truncated or duplicated differs from it.
fn pattern(length: usize) -> Vec<u8> {
    (0..length).map(|index| (index % 251) as u8).collect()
}

#[test]
fn a_result_larger_than_one_pipe_buffer_arrives_intact() {
    // 300 000 bytes is several times the 64 KiB a pipe holds, so the child blocks on a full pipe
    // until the parent reads: a parent that only waited would deadlock here until the patience ran
    // out. Compared byte for byte.
    let expected = pattern(300_000);
    let sent = expected.clone();

    let result = fork_for_bytes(no_drop, PATIENCE, 1_000_000, move |sink| {
        sink.write_all(&sent).map_err(|_| PrivError::WorkFailed)
    });

    assert_eq!(result, Ok(expected));
}

#[test]
fn a_result_exactly_at_the_cap_is_returned() {
    let expected = pattern(5_000);
    let sent = expected.clone();

    let result = fork_for_bytes(no_drop, PATIENCE, 5_000, move |sink| {
        sink.write_all(&sent).map_err(|_| PrivError::WorkFailed)
    });

    assert_eq!(result, Ok(expected));
}

#[test]
fn a_result_one_byte_over_the_cap_is_refused() {
    let sent = pattern(5_001);

    let result = fork_for_bytes(no_drop, PATIENCE, 5_000, move |sink| {
        sink.write_all(&sent).map_err(|_| PrivError::WorkFailed)
    });

    assert_eq!(result, Err(PrivError::ResultTooLarge));
}

#[test]
fn any_byte_at_all_is_too_much_for_a_cap_of_zero() {
    let result = fork_for_bytes(no_drop, PATIENCE, 0, |sink| {
        sink.write_all(b"x").map_err(|_| PrivError::WorkFailed)
    });

    assert_eq!(result, Err(PrivError::ResultTooLarge));
}

#[test]
fn a_child_that_writes_nothing_and_succeeds_returns_an_empty_result() {
    let result = fork_for_bytes(no_drop, PATIENCE, 10, |_sink| Ok(()));

    assert_eq!(result, Ok(Vec::new()));
}

#[test]
fn a_child_that_overruns_the_cap_is_killed_and_reaped() {
    // The child records its pid, writes past the cap, then sleeps far longer than the test. If the
    // parent only stopped READING, the child would end by itself on the broken pipe; if it
    // killed without collecting, the pid would linger as a zombie. Signal 0 reaches a zombie and
    // fails with ESRCH only once the process has been collected, so ESRCH is the proof of both.
    let directory = tempfile::tempdir().unwrap();
    let pid_file = directory.path().join("pid");
    let recorded = pid_file.clone();

    let result = fork_for_bytes(no_drop, PATIENCE, 100, move |sink| {
        std::fs::write(&recorded, std::process::id().to_string())
            .map_err(|_| PrivError::WorkFailed)?;
        sink.write_all(&pattern(101))
            .map_err(|_| PrivError::WorkFailed)?;
        std::thread::sleep(Duration::from_secs(600));
        Ok(())
    });

    assert_eq!(result, Err(PrivError::ResultTooLarge));
    let pid: i32 = std::fs::read_to_string(&pid_file).unwrap().parse().unwrap();
    let pid = rustix::process::Pid::from_raw(pid).unwrap();
    let probe = rustix::process::test_kill_process(pid);
    assert_eq!(
        probe.err(),
        Some(rustix::io::Errno::SRCH),
        "the over-producing child must be gone, not merely stopped"
    );
}

#[test]
fn a_child_that_writes_and_then_fails_returns_no_bytes() {
    let result = fork_for_bytes(no_drop, PATIENCE, 1_000, |sink| {
        sink.write_all(b"a half-finished listing")
            .map_err(|_| PrivError::WorkFailed)?;
        Err(PrivError::WorkFailed)
    });

    assert_eq!(result, Err(PrivError::WorkFailed));
}

#[test]
fn a_child_that_writes_and_then_panics_returns_no_bytes() {
    let result = fork_for_bytes(no_drop, PATIENCE, 1_000, |sink| {
        sink.write_all(b"partial")
            .map_err(|_| PrivError::WorkFailed)?;
        panic!("a planted failure after the child has already written");
    });

    assert_eq!(result, Err(PrivError::WorkFailed));
}

#[test]
fn a_child_that_is_killed_after_writing_returns_no_bytes() {
    let result = fork_for_bytes(no_drop, PATIENCE, 1_000, |sink| {
        sink.write_all(b"partial")
            .map_err(|_| PrivError::WorkFailed)?;
        // The parent's own kill is not involved: the child ends itself with SIGKILL.
        rustix::process::kill_process(rustix::process::getpid(), rustix::process::Signal::KILL)
            .map_err(|_| PrivError::WorkFailed)?;
        std::thread::sleep(Duration::from_secs(600));
        Ok(())
    });

    assert_eq!(
        result,
        Err(PrivError::ChildSignalled {
            signal: rustix::process::Signal::KILL.as_raw()
        })
    );
}

#[test]
fn a_silent_child_is_killed_when_the_patience_runs_out() {
    let started = Instant::now();

    let result = fork_for_bytes(no_drop, Duration::from_millis(300), 1_000, |_sink| {
        std::thread::sleep(Duration::from_secs(600));
        Ok(())
    });

    assert_eq!(result, Err(PrivError::ChildTimedOut));
    assert!(
        started.elapsed() < Duration::from_secs(25),
        "the give-up must come from the patience, not from the child finishing"
    );
}

#[test]
fn a_child_that_writes_and_then_hangs_returns_no_bytes() {
    let result = fork_for_bytes(no_drop, Duration::from_millis(300), 1_000, |sink| {
        sink.write_all(b"some of it")
            .map_err(|_| PrivError::WorkFailed)?;
        std::thread::sleep(Duration::from_secs(600));
        Ok(())
    });

    assert_eq!(result, Err(PrivError::ChildTimedOut));
}

#[test]
fn a_child_whose_drop_failed_returns_no_bytes_and_runs_no_work() {
    let directory = tempfile::tempdir().unwrap();
    let marker = directory.path().join("work-ran");
    let touched = marker.clone();

    let result = fork_for_bytes(
        || Err(EXIT_DROP_SETUID_FAILED),
        PATIENCE,
        1_000,
        move |sink| {
            std::fs::write(&touched, b"x").map_err(|_| PrivError::WorkFailed)?;
            sink.write_all(b"should never be seen")
                .map_err(|_| PrivError::WorkFailed)
        },
    );

    assert_eq!(result, Err(PrivError::DropFailed { syscall: "setuid" }));
    assert!(!marker.exists(), "work must not run when the drop failed");
}

/// What a child counts when asked how many descriptors above stdio it holds.
///
/// Read through `/proc/self/fd`, which lists the directory handle used to read it as well, so the
/// channel plus that handle is two; the test below states that.
fn descriptors_above_stdio_in_this_process() -> usize {
    std::fs::read_dir("/proc/self/fd")
        .unwrap()
        .filter_map(|entry| entry.ok()?.file_name().to_str()?.parse::<i32>().ok())
        .filter(|&number| number >= 3)
        .count()
}

#[test]
fn the_child_holds_its_end_of_the_channel_and_nothing_else_the_parent_had_open() {
    // The parent holds extra descriptors, which a fork copies. After the sweep the child must find
    // the channel and the directory handle it is reading with, and none of these.
    let _open_in_the_parent = [
        std::fs::File::open("/dev/null").unwrap(),
        std::fs::File::open("/dev/null").unwrap(),
        std::fs::File::open("/dev/null").unwrap(),
    ];
    assert!(
        descriptors_above_stdio_in_this_process() >= 4,
        "the control: the parent really does hold several descriptors"
    );

    let result = fork_for_bytes(no_drop, PATIENCE, 16, |sink| {
        let count = u8::try_from(descriptors_above_stdio_in_this_process()).unwrap_or(u8::MAX);
        sink.write_all(&[count]).map_err(|_| PrivError::WorkFailed)
    });

    assert_eq!(result, Ok(vec![2]));
}
