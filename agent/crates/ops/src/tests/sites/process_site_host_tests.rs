//! Tests for the `process_site_host` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `process_site_host.rs` declares this
//! file with `#[path]`, which keeps it a child module.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use super::ProcessSiteHost;
use crate::safe_write::ConfigHost;

/// The account name no host has, passed to `id` so it exits non-zero.
const ABSENT_ACCOUNT: &str = "maran-no-such-account-for-a-test";

// The host runs only what a distribution adapter named, as an `AdapterBinary`, a type
// no test can construct, so the two tests below use the adapter's `id` (coreutils, on
// both families). That a program which cannot be started is reported as a failure to
// START it, and a bound as a bound, is the mapping `SafeWriteError::from_spawn` owns,
// and `safe_write_error_tests.rs` drives it with a constructed spawn error.

/// A program that STARTED and then refused is an outcome, not a spawn failure.
///
/// A missing or unexecutable binary once came back as a failed reload, so an operator
/// read "configuration reload failed" and went looking at a config file. The inverse
/// control for the mapping is that a refusal is NOT mistaken for the other thing: the
/// host returns a non-zero status as an outcome and leaves its meaning to the protocol
/// above it, which turns it into a failed validation or a failed reload
/// (`render_validate_swap_tests.rs`).
#[test]
fn a_program_that_started_and_failed_is_an_outcome_and_not_a_spawn_failure() {
    let host = ProcessSiteHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = host
        .run(&id, &["-u", "--", ABSENT_ACCOUNT], CallClass::Query)
        .unwrap();

    assert_ne!(
        outcome.status, 0,
        "`id` of an absent account exits non-zero"
    );
}

/// The positive control: the same call for an account that exists succeeds.
#[test]
fn a_program_that_started_and_succeeded_returns_its_output() {
    let host = ProcessSiteHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = host
        .run(&id, &["-u", "--", "root"], CallClass::Query)
        .unwrap();

    assert_eq!(outcome.status, 0);
    assert_eq!(outcome.stdout.trim(), "0");
}
