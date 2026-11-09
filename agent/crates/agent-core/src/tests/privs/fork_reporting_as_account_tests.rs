//! The reporting fork: figures out, an upload in, refusals typed, and every malformed ending refused.
//!
//! A test is not root and cannot drop to another account, so these run through the private seam
//! with a drop step that succeeds or fails on demand. Each refusal sits beside the acceptance
//! that proves the function is not refusing everything.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs;
use std::io;
use std::time::Duration;

use super::fork_reporting;
use crate::privs::child_context::ChildContext;
use crate::privs::child_exit_status::EXIT_DROP_SETUID_FAILED;
use crate::privs::child_input_writer::ChildInputWriter;
use crate::privs::child_refusal::ChildRefusal;
use crate::privs::child_report::ChildReport;
use crate::privs::child_tally::ChildTally;
use crate::privs::input_frame::InputFrame;
use crate::privs::priv_error::PrivError;

/// Long enough for a loaded runner, so a slow machine cannot turn an acceptance into a timeout.
const PATIENCE: Duration = Duration::from_secs(30);

/// Grace for a child expected to exit by itself.
const GRACE: Duration = Duration::from_secs(10);

/// A drop step that succeeds: the test is not root, and the drop is not what is under test.
fn no_drop() -> Result<(), i32> {
    Ok(())
}

/// A recognisable pattern of `length` bytes.
fn pattern(length: usize) -> Vec<u8> {
    (0..length).map(|index| (index % 251) as u8).collect()
}

/// Reads frames until the stream ends; `Some(bytes)` when it was completed, `None` when aborted.
fn receive(context: &mut ChildContext<'_>) -> Option<Vec<u8>> {
    let mut received = Vec::new();
    loop {
        match context.read_frame() {
            InputFrame::Data(block) => received.extend_from_slice(block),
            InputFrame::Complete => return Some(received),
            InputFrame::Aborted => return None,
        }
    }
}

#[test]
fn the_two_figures_of_a_successful_job_arrive() {
    let result = fork_reporting(no_drop, PATIENCE, GRACE, None, |_| {
        Ok(ChildTally::new(7, u64::MAX))
    });

    assert_eq!(
        result,
        Ok(ChildReport {
            first: 7,
            second: u64::MAX
        })
    );
}

#[test]
fn a_refusal_number_arrives_as_work_refused() {
    let result = fork_reporting(no_drop, PATIENCE, GRACE, None, |_| {
        Err(ChildRefusal::new(13))
    });

    assert_eq!(result, Err(PrivError::WorkRefused { reason: 13 }));
}

#[test]
fn the_edges_of_the_refusal_range_are_accepted() {
    for code in [1_u8, 63] {
        let result = fork_reporting(no_drop, PATIENCE, GRACE, None, move |_| {
            Err(ChildRefusal::new(code))
        });
        assert_eq!(result, Err(PrivError::WorkRefused { reason: code }));
    }
}

#[test]
fn a_refusal_outside_one_to_sixty_three_is_a_malformed_report_not_a_success() {
    for code in [0_u8, 64, 255] {
        let result = fork_reporting(no_drop, PATIENCE, GRACE, None, move |_| {
            Err(ChildRefusal::new(code))
        });
        assert_eq!(result, Err(PrivError::WorkFailed), "{code}");
    }
}

#[test]
fn a_child_that_dies_without_reporting_is_a_plain_failure() {
    let result = fork_reporting(no_drop, PATIENCE, GRACE, None, |_| {
        // The work returns an error path the fork cannot report: exit via a panic.
        std::process::abort()
    });

    assert!(matches!(result, Err(PrivError::ChildSignalled { .. })));
}

#[test]
fn a_failed_drop_does_no_work_and_is_reported() {
    let dir = tempfile::tempdir().unwrap();
    let marker = dir.path().join("ran");
    let marked = marker.clone();

    let result = fork_reporting(
        || Err(EXIT_DROP_SETUID_FAILED),
        PATIENCE,
        GRACE,
        None,
        move |_| {
            fs::write(&marked, b"x").unwrap();
            Ok(ChildTally::new(0, 0))
        },
    );

    assert_eq!(result, Err(PrivError::DropFailed { syscall: "setuid" }));
    assert!(!marker.exists());
}

#[test]
fn an_upload_larger_than_one_socket_buffer_arrives_intact_and_complete() {
    let dir = tempfile::tempdir().unwrap();
    let target = dir.path().join("received");
    let saved = target.clone();
    let expected = pattern(700_000);
    let sent = expected.clone();

    let mut feed = move |writer: &mut ChildInputWriter<'_>| {
        writer.send(&sent)?;
        writer.complete()
    };
    let result = fork_reporting(no_drop, PATIENCE, GRACE, Some(&mut feed), move |context| {
        let received = receive(context).ok_or(ChildRefusal::new(1))?;
        fs::write(saved, &received).map_err(|_| ChildRefusal::new(2))?;
        Ok(ChildTally::new(received.len() as u64, 0))
    });

    assert_eq!(
        result,
        Ok(ChildReport {
            first: 700_000,
            second: 0
        })
    );
    assert_eq!(fs::read(target).unwrap(), expected);
}

#[test]
fn a_stream_that_ends_without_the_terminator_is_seen_as_aborted() {
    let mut feed = |writer: &mut ChildInputWriter<'_>| writer.send(b"partial");
    let result = fork_reporting(
        no_drop,
        PATIENCE,
        GRACE,
        Some(&mut feed),
        |context| match receive(context) {
            Some(_) => Ok(ChildTally::new(1, 0)),
            None => Err(ChildRefusal::new(9)),
        },
    );

    assert_eq!(result, Err(PrivError::WorkRefused { reason: 9 }));
}

#[test]
fn a_feed_that_fails_aborts_the_upload_and_reports_the_channel() {
    let dir = tempfile::tempdir().unwrap();
    let marker = dir.path().join("saw_abort");
    let marked = marker.clone();

    let mut feed = |writer: &mut ChildInputWriter<'_>| {
        writer.send(b"partial")?;
        Err(io::Error::from_raw_os_error(libc::EIO))
    };
    let result = fork_reporting(no_drop, PATIENCE, GRACE, Some(&mut feed), move |context| {
        if receive(context).is_none() {
            fs::write(&marked, b"x").unwrap();
        }
        // A refusal outside the closed range is a child that gave up without a reason: a real
        // refusal would win over the feed's error (the child's answer is the better reason).
        Err(ChildRefusal::new(200))
    });

    assert_eq!(result, Err(PrivError::ChannelFailed { errno: libc::EIO }));
    assert!(marker.exists(), "the child must see the abort");
}

#[test]
fn a_child_that_never_reads_makes_the_feed_time_out() {
    let mut feed = |writer: &mut ChildInputWriter<'_>| writer.send(&pattern(4_000_000));
    let result = fork_reporting(
        no_drop,
        Duration::from_millis(300),
        Duration::from_millis(200),
        Some(&mut feed),
        |_| {
            std::thread::sleep(Duration::from_secs(20));
            Ok(ChildTally::new(0, 0))
        },
    );

    assert!(
        matches!(
            result,
            Err(PrivError::ChannelFailed {
                errno: libc::ETIMEDOUT
            } | PrivError::ChildTimedOut)
        ),
        "{result:?}"
    );
}

#[test]
fn a_refusal_mid_upload_wins_over_the_broken_pipe_it_causes() {
    let mut feed = |writer: &mut ChildInputWriter<'_>| writer.send(&pattern(4_000_000));
    let result = fork_reporting(no_drop, PATIENCE, GRACE, Some(&mut feed), |_| {
        Err(ChildRefusal::new(13))
    });

    assert_eq!(result, Err(PrivError::WorkRefused { reason: 13 }));
}

#[test]
fn an_oversized_frame_is_refused_by_the_child_as_aborted() {
    let mut feed = |writer: &mut ChildInputWriter<'_>| {
        // A raw header claiming 1 GiB: only a hostile or broken parent writes it.
        let mut channel: &std::os::unix::net::UnixStream = writer.raw_channel_for_tests();
        io::Write::write_all(&mut channel, &(1_u32 << 30).to_be_bytes())
    };
    let result = fork_reporting(
        no_drop,
        PATIENCE,
        GRACE,
        Some(&mut feed),
        |context| match context.read_frame() {
            InputFrame::Aborted => Err(ChildRefusal::new(5)),
            _ => Ok(ChildTally::new(0, 0)),
        },
    );

    assert_eq!(result, Err(PrivError::WorkRefused { reason: 5 }));
}
