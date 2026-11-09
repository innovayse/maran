//! The child the fork creates: what it has set about itself before any work runs.
//!
//! A test is not root and cannot drop to another account, so the drop step is one that succeeds.
//! This module is one of those that may hold `unsafe`, which is why the observation of the dump
//! flag lives here and not beside the byte channel's tests.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::{Read, Write};
use std::os::fd::AsRawFd;

use super::fork_and_run;
use crate::privs::priv_error::PrivError;
use crate::privs::wait_for_child::wait_for;

/// A drop step that succeeds: the test is not root, and the drop is not what is under test.
fn no_drop() -> Result<(), i32> {
    Ok(())
}

/// The dump flag of the calling process, as `prctl(PR_GET_DUMPABLE)` reports it.
fn dumpable_in_this_process() -> u8 {
    // SAFETY: `prctl(PR_GET_DUMPABLE)` takes integers only and touches no memory.
    let flag = unsafe { libc::prctl(libc::PR_GET_DUMPABLE, 0, 0, 0, 0) };
    u8::try_from(flag).unwrap_or(u8::MAX)
}

#[test]
fn the_child_is_not_dumpable_so_the_customers_other_processes_cannot_reach_its_channel() {
    // The control: this process is dumpable, so a child that reads 0 changed it and did not
    // merely inherit a process that was never dumpable.
    if dumpable_in_this_process() != 1 {
        eprintln!(
            "skipped: the test process is not dumpable, so the child's 0 would prove nothing"
        );
        return;
    }

    let (mut reader, writer) = std::io::pipe().unwrap();
    let child = fork_and_run(no_drop, Some(writer.as_raw_fd()), || {
        (&writer)
            .write_all(&[dumpable_in_this_process()])
            .map_err(|_| PrivError::WorkFailed)
    })
    .unwrap();
    drop(writer);

    let mut reported = Vec::new();
    reader.read_to_end(&mut reported).unwrap();
    wait_for(child).unwrap();

    assert_eq!(reported, vec![0]);
    assert_eq!(
        dumpable_in_this_process(),
        1,
        "the parent must stay as it was"
    );
}
