//! Tests for the `process_system_host` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `process_system_host.rs` declares this
//! file with `#[path]`, which keeps it a child module.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::debian::debian_adapter::DebianAdapter;

use super::{LOCALE_VALUE, LOCALE_VARIABLE, ProcessSystemHost};
use crate::accounts::system_host::SystemHost;

/// The program that prints the environment a child was actually given.
const ENV_BINARY: &str = "/usr/bin/env";

/// A spawn carries `LC_ALL=C`, whatever the daemon's own environment holds.
///
/// This is the witness the fix had none of. `remove_crontab` decides "there was no
/// crontab to remove" by reading `crontab`'s own message, so the language that
/// message is printed in is part of the decision — and under a non-English locale a
/// refusal it cannot recognise aborts the deletion BEFORE `userdel`, making every
/// account without a crontab undeletable. Asserting on the child's real environment
/// rather than on the builder is the point: the question is what `execve` received.
///
/// Remove the `.env` line and this test fails on a host whose own environment sets
/// no `LC_ALL` — which is the agent's unit, since nothing sets a locale on it.
#[test]
fn a_spawned_program_runs_under_the_pinned_locale() {
    // The adapter is irrelevant here — this asserts on the spawn, which every
    // operation shares — so the cheapest concrete one is used.
    let host = ProcessSystemHost::new(&DebianAdapter);
    let outcome = host.run(ENV_BINARY, &[]).unwrap();

    assert_eq!(
        outcome.status, 0,
        "{ENV_BINARY} did not run: {}",
        outcome.stderr
    );
    assert!(
        outcome
            .stdout
            .lines()
            .any(|line| line == format!("{LOCALE_VARIABLE}={LOCALE_VALUE}")),
        "the child did not receive {LOCALE_VARIABLE}={LOCALE_VALUE}; it saw:\n{}",
        outcome.stdout
    );
}
