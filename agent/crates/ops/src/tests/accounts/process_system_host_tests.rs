//! Tests for the `process_system_host` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `process_system_host.rs` declares this
//! file with `#[path]`, which keeps it a child module.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::apply_child_environment::{
    CHILD_ENVIRONMENT, LOCALE_VALUE, LOCALE_VARIABLE,
};
use maran_distro::debian::debian_adapter::DebianAdapter;

use super::ProcessSystemHost;
use crate::accounts::system_host::SystemHost;

/// The program that prints the environment a child was actually given.
const ENV_BINARY: &str = "/usr/bin/env";

/// The variable the outer half of the witness uses to tell the inner half it is
/// the inner half.
///
/// The two halves are two PROCESSES, and the reason is `rules/rust.md`:
/// poisoning this process's own environment would mean `std::env::set_var`,
/// which is `unsafe`, and `privs` is the only place in this workspace where
/// `unsafe` is allowed. A child spawned with `Command::env` needs none, and it
/// is also the more honest model — the agent inherits an environment it did not
/// choose, exactly as this child does.
const INNER_HALF_MARKER: &str = "MARAN_TEST_SPAWN_ENVIRONMENT_INNER";

/// The test-harness filter that names the inner half.
///
/// The full module path and not the bare function name: `--exact` matches the
/// whole path, and a filter that matches nothing runs zero tests and exits 0 —
/// which is why the outer half also checks that one test actually ran.
const INNER_HALF_CASE: &str = "accounts::process_system_host::tests::a_spawned_program_receives_only_the_declared_environment";

/// The two variables that are not settings but instructions.
///
/// `LD_PRELOAD` is honoured by the dynamic loader of every child, shell or not.
/// `BASH_ENV` names a file bash sources before a non-interactive script, and on
/// the RHEL family `/bin/sh` IS bash — which is what `tar`'s
/// `--use-compress-program` forks on the write side. Values that point at
/// nothing, because the assertion is that they do not ARRIVE; a real payload
/// would only make a failure harder to read.
const HOSTILE_ENVIRONMENT: [(&str, &str); 3] = [
    ("LD_PRELOAD", "/nonexistent/maran-test-payload.so"),
    ("BASH_ENV", "/nonexistent/maran-test-payload.sh"),
    ("TAR_OPTIONS", "--absolute-names"),
];

/// A spawned program's environment is exactly `CHILD_ENVIRONMENT`, whatever the
/// daemon inherited.
///
/// This is the witness the `LC_ALL` pin already had, widened to the property the
/// pin was one entry of. It asserts on the child's REAL environment — what
/// `execve` received — rather than on the builder, and it asserts the whole set
/// rather than one member, so an inherited variable of any kind fails it. Remove
/// the `env_clear()` in `apply_child_environment` and this test goes red on the
/// dozens of variables a shell, a CI runner or `cargo` leaves behind, before it
/// ever gets to the three planted below.
///
/// The outer half plants `LD_PRELOAD`, `BASH_ENV` and `TAR_OPTIONS` in a child
/// copy of this same test binary and asks it for this same case by name; the
/// inner half is the one that spawns `/usr/bin/env` and judges what came out.
/// One test that re-enters itself rather than a pair, so that a plain `cargo
/// test` can never reach an inner half with no poisoned parent and report the
/// vacuous version as a pass.
///
/// What it does NOT prove: that no program the agent starts NEEDS one of the
/// cleared variables. Nothing an assertion can do settles that; the polygon
/// suites that drive a real `useradd`, `crontab`, `nft` and package manager are
/// what would catch it.
#[test]
fn a_spawned_program_receives_only_the_declared_environment() {
    if std::env::var(INNER_HALF_MARKER).is_err() {
        let output = std::process::Command::new(std::env::current_exe().expect("this test binary"))
            .args([
                INNER_HALF_CASE,
                "--exact",
                "--nocapture",
                "--test-threads=1",
            ])
            .env(INNER_HALF_MARKER, "1")
            .envs(HOSTILE_ENVIRONMENT)
            .output()
            .expect("the inner half of this case starts");

        let reported = String::from_utf8_lossy(&output.stdout);
        assert!(
            output.status.success(),
            "the inner half failed:\n{reported}\n{}",
            String::from_utf8_lossy(&output.stderr)
        );
        // A filter that matches nothing runs zero tests and exits 0, which
        // would report this whole witness as a pass while asserting nothing.
        assert!(
            reported.contains("1 passed"),
            "the inner half ran no test — {INNER_HALF_CASE} no longer names it:\n{reported}"
        );
        return;
    }

    // The adapter is irrelevant here — this asserts on the spawn, which every
    // operation shares — so the cheapest concrete one is used.
    let host = ProcessSystemHost::new(&DebianAdapter);
    let outcome = host.run(ENV_BINARY, &[]).unwrap();

    assert_eq!(
        outcome.status, 0,
        "{ENV_BINARY} did not run: {}",
        outcome.stderr
    );

    let mut seen: Vec<&str> = outcome.stdout.lines().collect();
    seen.sort_unstable();
    let mut declared: Vec<String> = CHILD_ENVIRONMENT
        .iter()
        .map(|(name, value)| format!("{name}={value}"))
        .collect();
    declared.sort_unstable();

    assert_eq!(
        seen, declared,
        "the child's environment is not the declared one; it saw:\n{}",
        outcome.stdout
    );
}
/// A spawn carries `LC_ALL=C`, whatever the daemon's own environment holds.
///
/// This is the witness the fix had none of. `remove_crontab` decides "there was no
/// crontab to remove" by reading `crontab`'s own message, so the language that
/// message is printed in is part of the decision — and under a non-English locale a
/// refusal it cannot recognise aborts the deletion BEFORE `userdel`, making every
/// account without a crontab undeletable. Asserting on the child's real environment
/// rather than on the builder is the point: the question is what `execve` received.
///
/// The pin now lives in `maran_agent_core::utils::spawn_argv`, which is where every
/// host's spawn goes, so this test guards it for all of them and not only for this
/// one. Remove the `.env` line there and this test fails on a host whose own
/// environment sets no `LC_ALL` — which is the agent's unit, since nothing sets a
/// locale on it.
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
