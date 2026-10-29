//! Tests for [`kill_group_and_reap`]: the leader is killed and collected.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::process::Stdio;
use std::time::Instant;

use super::{REAP_CEILING, kill_group_and_reap};
use crate::utils::launch_child::launch_child;

/// A child that would run for half a minute is killed, collected (its `/proc`
/// entry is gone and not a zombie) and the call is far quicker than the reap
/// ceiling.
#[test]
fn a_running_leader_is_killed_and_collected() {
    let mut child = launch_child(
        Path::new("/usr/bin/sleep"),
        &["30"],
        Stdio::null(),
        Stdio::null(),
        Stdio::null(),
    )
    .unwrap();
    let pid = child.id();
    let started = Instant::now();

    let collected = kill_group_and_reap(&mut child);
    let elapsed = started.elapsed();
    let still_listed = Path::new(&format!("/proc/{pid}")).exists();

    assert!(collected, "the leader was not collected");
    assert!(!still_listed, "the leader is still a zombie or still alive");
    assert!(
        elapsed < REAP_CEILING,
        "killing a plain child took {elapsed:?}"
    );
}

/// A leader that already exited is collected all the same.
#[test]
fn an_already_exited_leader_is_collected() {
    let mut child = launch_child(
        Path::new("/usr/bin/true"),
        &[],
        Stdio::null(),
        Stdio::null(),
        Stdio::null(),
    )
    .unwrap();
    let pid = child.id();

    let collected = kill_group_and_reap(&mut child);

    assert!(collected);
    assert!(!Path::new(&format!("/proc/{pid}")).exists());
}
