//! The real logins host starting a real program through the bounded core.
//!
//! The program is a real adapter's `id`: an `AdapterBinary` cannot be built
//! outside the distro crate, and `id` exists at one path on every host this
//! suite runs on and needs no privilege.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use crate::logins::logins_host::LoginsHost;
use crate::logins::process_logins_host::ProcessLoginsHost;

#[test]
fn a_program_the_adapter_names_is_run_and_its_output_comes_back() {
    let host = ProcessLoginsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = host
        .run(&id, &["-u"], CallClass::Query)
        .expect("id must run");

    assert_eq!(outcome.status, 0);
    assert!(
        outcome.stdout.trim().parse::<u32>().is_ok(),
        "id -u prints a number: {:?}",
        outcome.stdout
    );
}

#[test]
fn a_program_that_exits_non_zero_is_an_outcome_and_never_an_error() {
    // The status is the caller's domain decision, so a refusal must reach it as
    // a status and not as a bound or a start failure.
    let host = ProcessLoginsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = host
        .run(
            &id,
            &["-u", "--", "no-such-maran-user-90"],
            CallClass::Query,
        )
        .expect("a refusal is an outcome");

    assert_ne!(outcome.status, 0);
}

#[test]
fn the_host_runs_every_program_under_the_class_its_caller_names() {
    // The host owns no class of its own: a literal here would override the
    // caller's choice for every operation at once.
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../logins/process_logins_host.rs"
    ));

    assert!(classes.is_empty(), "{classes:?}");
}
