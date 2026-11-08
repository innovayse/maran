//! Tests for the `process_system_host` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise (rules/testing.md). `process_system_host.rs` declares this
//! file with `#[path]`, which keeps it a child module.
//!
//! The witness that a spawned program receives only the declared environment (and
//! so the pinned `LC_ALL=C`) used to live here, because this host was where the
//! spawn lived. It moved with the spawn to `agent-core`'s `spawn_argv_tests.rs`:
//! the host now takes an `AdapterBinary`, a type no test can build, and no adapter
//! names a program that prints its environment. What is tested here is what this
//! host adds to the spawn: the adapter's `id` as the existence question, and a
//! refusal reported as an outcome.

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::debian::debian_adapter::DebianAdapter;
use maran_distro::{DistroFamily, adapter_for};

use super::ProcessSystemHost;
use crate::accounts::system_host::SystemHost;

/// A name no host has an account for.
const ABSENT_ACCOUNT: &str = "maran-no-such-account-for-a-test";

/// An account every Linux host has.
const PRESENT_ACCOUNT: &str = "root";

/// A user the host knows is found.
#[test]
fn a_user_the_host_knows_is_found_through_the_adapters_id() {
    let host = ProcessSystemHost::new(&DebianAdapter);

    assert!(host.user_exists(PRESENT_ACCOUNT).unwrap());
}

/// The inverse control: a name no host has is reported absent, not as an error.
#[test]
fn a_user_the_host_does_not_know_is_absent_and_not_an_error() {
    let host = ProcessSystemHost::new(&DebianAdapter);

    assert!(!host.user_exists(ABSENT_ACCOUNT).unwrap());
}

/// A program that started and refused comes back as an outcome.
#[test]
fn a_program_that_started_and_refused_is_an_outcome_and_not_an_error() {
    let host = ProcessSystemHost::new(&DebianAdapter);
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
fn a_program_that_succeeds_returns_its_output() {
    let host = ProcessSystemHost::new(&DebianAdapter);
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = host
        .run(&id, &["-u", "--", PRESENT_ACCOUNT], CallClass::Query)
        .unwrap();

    assert_eq!(outcome.status, 0);
    assert_eq!(outcome.stdout.trim(), "0");
}

/// Removing a drop-in that is there takes it away.
#[test]
fn a_limits_file_that_is_there_is_removed() {
    let directory = tempfile::tempdir().unwrap();
    let file = directory.path().join("maran-acme.conf");
    std::fs::write(&file, "acme - nofile 1\n").unwrap();
    let host = ProcessSystemHost::new(&DebianAdapter);

    host.remove_limits_file(&file).unwrap();

    assert!(!file.exists());
}

/// Removing converges: a drop-in that was never written is success, not a failure.
#[test]
fn a_limits_file_that_is_not_there_is_removed_without_complaint() {
    let directory = tempfile::tempdir().unwrap();
    let host = ProcessSystemHost::new(&DebianAdapter);

    assert!(
        host.remove_limits_file(&directory.path().join("maran-absent.conf"))
            .is_ok()
    );
}

/// The failing control: a path that cannot be removed for another reason is the
/// limits-file failure, so a deletion stops instead of leaving the file behind.
#[test]
fn a_limits_path_that_cannot_be_removed_is_the_limits_file_failure() {
    let directory = tempfile::tempdir().unwrap();
    let host = ProcessSystemHost::new(&DebianAdapter);

    // A directory is not removable with `remove_file`, and is not "not found".
    let refusal = host.remove_limits_file(directory.path());

    assert!(
        matches!(
            refusal,
            Err(crate::accounts::AccountError::LimitsFileFailed)
        ),
        "expected LimitsFileFailed, got {refusal:?}"
    );
}
