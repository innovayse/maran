//! Tests for [`has_exited`]: it looks at a child without collecting it.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::process::Stdio;
use std::time::Duration;

use super::has_exited;
use crate::utils::launch_child::launch_child;
use crate::utils::process_probe::{kill_pid, wait_until};

/// A running child is not exited; an exited one is, and it stays a zombie
/// afterwards (so its pid cannot be recycled) however often it is looked at.
#[test]
fn an_exit_is_observed_without_collecting_the_child() {
    let mut child = launch_child(
        Path::new("/usr/bin/sleep"),
        &["30"],
        Stdio::null(),
        Stdio::null(),
        Stdio::null(),
    )
    .unwrap();
    let pid = child.id();

    let while_running = has_exited(&child).unwrap();
    kill_pid(pid);
    let seen = wait_until(Duration::from_secs(10), || {
        has_exited(&child).unwrap_or(false)
    });
    let again = has_exited(&child);
    let stat = std::fs::read_to_string(format!("/proc/{pid}/stat")).unwrap_or_default();
    let _ = child.wait();

    assert!(!while_running, "a running child was reported as exited");
    assert!(seen, "the exit was never observed");
    assert!(
        matches!(again, Ok(true)),
        "looking twice must give the same answer, got {again:?}"
    );
    assert!(
        stat.rsplit_once(')')
            .is_some_and(|(_, rest)| rest.trim_start().starts_with('Z')),
        "the exited child must still be an uncollected zombie, stat was {stat:?}"
    );
}
