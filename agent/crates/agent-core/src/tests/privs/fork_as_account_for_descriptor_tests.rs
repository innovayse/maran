//! The descriptor hand-off end to end: a forked child opens a file, the parent receives it.
//!
//! A test is not root, so these run through the private seam with a drop step that succeeds or
//! fails on demand, and — the reason the seam takes the child's whole action — with children that
//! do what a hostile or broken one would: send nothing, send and then fail, send and then hang.
//! The real drop is exercised by `crates/agent/tests/privileges_on_a_real_host.rs` in the polygon.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::fs::File;
use std::io::{Read, Write};
use std::os::fd::{AsFd, BorrowedFd};
use std::path::PathBuf;
use std::time::Duration;

use super::{fork_for_descriptor, hand_over};
use crate::privs::child_exit_status::EXIT_DROP_SETUID_FAILED;
use crate::privs::priv_error::PrivError;
use crate::privs::send_descriptor::send_descriptor;

/// The patience of a test whose child is expected to finish.
const PATIENCE: Duration = Duration::from_secs(30);

/// A drop step that succeeds: the test is not root, and the drop is not what is under test.
fn no_drop() -> Result<(), i32> {
    Ok(())
}

/// A file holding `contents` in a directory the caller keeps alive.
fn a_file_with(contents: &[u8]) -> (tempfile::TempDir, PathBuf) {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("file");
    std::fs::write(&path, contents).unwrap();
    (directory, path)
}

#[test]
fn a_file_the_child_opened_arrives_open_and_readable() {
    let (_directory, path) = a_file_with(b"opened by the child");

    let received = fork_for_descriptor(
        no_drop,
        PATIENCE,
        hand_over(move || File::open(&path).map_err(|_| PrivError::WorkFailed)),
    );

    let mut contents = String::new();
    received.unwrap().read_to_string(&mut contents).unwrap();
    assert_eq!(contents, "opened by the child");
}

#[test]
fn the_received_file_cannot_be_written_to() {
    let (_directory, path) = a_file_with(b"x");

    let mut received = fork_for_descriptor(
        no_drop,
        PATIENCE,
        hand_over(move || File::open(&path).map_err(|_| PrivError::WorkFailed)),
    )
    .unwrap();

    assert!(received.write(b"y").is_err());
}

#[test]
fn a_file_the_child_opened_for_writing_is_refused() {
    let (_directory, path) = a_file_with(b"x");

    let received = fork_for_descriptor(
        no_drop,
        PATIENCE,
        hand_over(move || {
            std::fs::OpenOptions::new()
                .write(true)
                .open(&path)
                .map_err(|_| PrivError::WorkFailed)
        }),
    );

    assert_eq!(received.unwrap_err(), PrivError::DescriptorNotReadOnly);
}

#[test]
fn a_child_whose_work_fails_reports_the_failure_and_not_a_missing_descriptor() {
    let received = fork_for_descriptor(no_drop, PATIENCE, hand_over(|| Err(PrivError::WorkFailed)));

    assert_eq!(received.unwrap_err(), PrivError::WorkFailed);
}

/// Issue #455: a closure that reports the entry absent comes back as `NoSuchEntry`, and the
/// inverse control: any other failure of the closure stays `WorkFailed`, however it is spelled.
#[test]
fn a_child_whose_entry_is_absent_reports_no_such_entry_and_every_other_failure_stays_work_failed() {
    let absent = fork_for_descriptor(no_drop, PATIENCE, hand_over(|| Err(PrivError::NoSuchEntry)));
    assert_eq!(absent.unwrap_err(), PrivError::NoSuchEntry);

    for other in [
        PrivError::WorkFailed,
        PrivError::NoSuchAccount,
        PrivError::ResultTooLarge,
        PrivError::UnexpectedExit { status: 79 },
    ] {
        let refused = fork_for_descriptor(no_drop, PATIENCE, hand_over(move || Err(other)));
        assert_eq!(refused.unwrap_err(), PrivError::WorkFailed);
    }
}

#[test]
fn a_child_that_succeeds_without_sending_anything_is_refused() {
    let received = fork_for_descriptor(no_drop, PATIENCE, |_channel: BorrowedFd<'_>| Ok(()));

    assert_eq!(received.unwrap_err(), PrivError::DescriptorNotReceived);
}

#[test]
fn a_child_that_sends_a_descriptor_and_then_fails_yields_no_file() {
    let (_directory, path) = a_file_with(b"x");

    let received = fork_for_descriptor(no_drop, PATIENCE, move |channel: BorrowedFd<'_>| {
        let file = File::open(&path).map_err(|_| PrivError::WorkFailed)?;
        send_descriptor(channel, file.as_fd()).map_err(|_| PrivError::WorkFailed)?;
        Err(PrivError::WorkFailed)
    });

    assert_eq!(received.unwrap_err(), PrivError::WorkFailed);
}

#[test]
fn a_child_that_sends_a_descriptor_and_then_hangs_is_killed_and_yields_no_file() {
    let (_directory, path) = a_file_with(b"x");

    let received = fork_for_descriptor(
        no_drop,
        Duration::from_millis(300),
        move |channel: BorrowedFd<'_>| {
            let file = File::open(&path).map_err(|_| PrivError::WorkFailed)?;
            send_descriptor(channel, file.as_fd()).map_err(|_| PrivError::WorkFailed)?;
            std::thread::sleep(Duration::from_secs(600));
            Ok(())
        },
    );

    assert_eq!(received.unwrap_err(), PrivError::ChildTimedOut);
}

#[test]
fn a_child_that_never_sends_is_killed_and_reaped_when_the_patience_runs_out() {
    // As for the byte channel: the child records its pid, and ESRCH on signal 0 proves it was
    // collected and not merely signalled.
    let directory = tempfile::tempdir().unwrap();
    let pid_file = directory.path().join("pid");
    let recorded = pid_file.clone();

    let received = fork_for_descriptor(
        no_drop,
        Duration::from_millis(300),
        move |_channel: BorrowedFd<'_>| {
            std::fs::write(&recorded, std::process::id().to_string())
                .map_err(|_| PrivError::WorkFailed)?;
            std::thread::sleep(Duration::from_secs(600));
            Ok(())
        },
    );

    assert_eq!(received.unwrap_err(), PrivError::ChildTimedOut);
    let pid: i32 = std::fs::read_to_string(&pid_file).unwrap().parse().unwrap();
    let pid = rustix::process::Pid::from_raw(pid).unwrap();
    assert_eq!(
        rustix::process::test_kill_process(pid).err(),
        Some(rustix::io::Errno::SRCH),
        "the silent child must be gone, not merely stopped"
    );
}

#[test]
fn a_child_whose_drop_failed_hands_over_nothing_and_runs_no_work() {
    let directory = tempfile::tempdir().unwrap();
    let marker = directory.path().join("work-ran");
    let touched = marker.clone();
    let (_held, path) = a_file_with(b"x");

    let received = fork_for_descriptor(
        || Err(EXIT_DROP_SETUID_FAILED),
        PATIENCE,
        hand_over(move || {
            std::fs::write(&touched, b"x").map_err(|_| PrivError::WorkFailed)?;
            File::open(&path).map_err(|_| PrivError::WorkFailed)
        }),
    );

    assert_eq!(
        received.unwrap_err(),
        PrivError::DropFailed { syscall: "setuid" }
    );
    assert!(!marker.exists(), "work must not run when the drop failed");
}
