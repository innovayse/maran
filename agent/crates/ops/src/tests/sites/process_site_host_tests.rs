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

use std::os::unix::fs::PermissionsExt;

use super::{AUTH_DIRECTORY_MODE, AUTH_FILE_MODE, ProcessSiteHost};
use crate::safe_write::ConfigHost;
use crate::sites::model::auth_file_placement::AuthFilePlacement;

/// The bits the agent's unit (`RestrictSUIDSGID=true`) makes the kernel refuse in a `chmod`.
const SPECIAL_BITS: u32 = 0o7000;

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

/// The name of the group the test process itself belongs to, which it may hand a directory to
/// without any privilege.
fn own_group_name(host: &ProcessSiteHost) -> String {
    let id = adapter_for(DistroFamily::Debian).id_binary();
    let outcome = host.run(&id, &["-gn"], CallClass::Query).unwrap();
    assert_eq!(outcome.status, 0, "`id -gn` names the process's own group");
    outcome.stdout.trim().to_owned()
}

/// The credential directory is given a mode the agent's sandbox lets it set (issue #498).
///
/// `RestrictSUIDSGID=true` makes the kernel answer EPERM to any `chmod` that carries a setuid,
/// setgid or sticky bit, and the directory used to be `2750`: every password-protected site on
/// a real host answered 500. The constants are the contract; the directory the preparation
/// makes is what the host is left with, so both are read.
#[test]
fn the_credential_directory_is_made_without_a_special_bit() {
    let host = ProcessSiteHost::new();
    let distro = adapter_for(DistroFamily::Debian);
    let getent = distro.getent_binary();
    let group = own_group_name(&host);
    let placement = AuthFilePlacement {
        group: &group,
        getent: &getent,
    };
    let parent = tempfile::tempdir().unwrap();
    let directory = parent.path().join("auth");

    host.prepare_auth_directory(&directory, &placement).unwrap();

    assert_eq!(AUTH_DIRECTORY_MODE & SPECIAL_BITS, 0);
    let mode = std::fs::metadata(&directory).unwrap().permissions().mode();
    assert_eq!(mode & 0o7777, AUTH_DIRECTORY_MODE);
}

/// The inverse control: a directory an earlier agent or an operator left with a setgid bit is put
/// right, and a mode the sandbox does accept is left as it is when the preparation is repeated.
#[test]
fn preparing_the_credential_directory_again_converges_on_the_same_mode() {
    let host = ProcessSiteHost::new();
    let distro = adapter_for(DistroFamily::Debian);
    let getent = distro.getent_binary();
    let group = own_group_name(&host);
    let placement = AuthFilePlacement {
        group: &group,
        getent: &getent,
    };
    let parent = tempfile::tempdir().unwrap();
    let directory = parent.path().join("auth");
    std::fs::create_dir(&directory).unwrap();
    std::fs::set_permissions(&directory, std::fs::Permissions::from_mode(0o2755)).unwrap();

    host.prepare_auth_directory(&directory, &placement).unwrap();
    host.prepare_auth_directory(&directory, &placement).unwrap();

    let mode = std::fs::metadata(&directory).unwrap().permissions().mode();
    assert_eq!(mode & 0o7777, AUTH_DIRECTORY_MODE);
}

/// The file is readable without owning the directory's group, because nothing hands it the group
/// any more; the directory's `0750` is what keeps everyone else out.
#[test]
fn the_credential_file_is_readable_by_the_web_server_through_the_directory_alone() {
    assert_eq!(AUTH_FILE_MODE & SPECIAL_BITS, 0);
    assert_ne!(
        AUTH_FILE_MODE & 0o004,
        0,
        "the web server's workers read it as other"
    );
    assert_eq!(AUTH_FILE_MODE & 0o022, 0, "only root writes it");
    assert_eq!(
        AUTH_DIRECTORY_MODE & 0o007,
        0,
        "no one outside the group enters the directory"
    );
}
