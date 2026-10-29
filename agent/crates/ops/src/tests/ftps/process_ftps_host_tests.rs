//! The real ftps host starting a real program through the bounded core.
//!
//! The program is a real adapter's `id`: an `AdapterBinary` cannot be built
//! outside the distro crate, and `id` exists at one path on every host this
//! suite runs on and needs no privilege.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::process_ftps_host::ProcessFtpsHost;

#[test]
fn a_program_the_adapter_names_is_run_and_its_output_comes_back() {
    let host = ProcessFtpsHost::new();
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
    let host = ProcessFtpsHost::new();
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
fn a_line_given_as_input_reaches_the_programs_standard_input() {
    // `openssl base64` reads standard input to its end and prints it encoded, so
    // the encoded line on stdout proves the bytes were delivered whole and the
    // pipe was closed after them.
    let host = ProcessFtpsHost::new();
    let openssl = adapter_for(DistroFamily::Debian).openssl_binary();

    let outcome = host
        .run_with_stdin(
            &openssl,
            &["base64", "-A"],
            "alice:s3cret",
            CallClass::Query,
        )
        .expect("openssl must run");

    assert_eq!(outcome.status, 0);
    assert_eq!(outcome.stdout, "YWxpY2U6czNjcmV0");
}

#[test]
fn a_program_that_exits_non_zero_through_the_stdin_path_is_an_outcome_and_never_an_error() {
    // The inverse of the bound: a refusal is the caller's domain decision and
    // must reach it as a status, not as a deadline or a start failure.
    let host = ProcessFtpsHost::new();
    // A program that READS its input and then refuses it: one that exits without
    // reading would race its own stdin pipe and fail the delivery instead.
    let openssl = adapter_for(DistroFamily::Debian).openssl_binary();
    let arguments = ["enc", "-d", "-aes-256-cbc", "-pass", "pass:x", "-a", "-A"];

    let outcome = host
        .run_with_stdin(&openssl, &arguments, "x", CallClass::Query)
        .expect("a refusal is an outcome");

    assert_ne!(outcome.status, 0);
}

#[test]
fn the_host_runs_every_program_under_the_class_its_caller_names() {
    // The host owns no class of its own: a literal here would override the
    // caller's choice for every operation at once, and the calls it serves run
    // from sixty-second queries to the package deadline.
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../ftps/process_ftps_host.rs"
    ));

    assert!(classes.is_empty(), "{classes:?}");
}

#[test]
fn the_config_write_protocol_runs_its_programs_through_the_same_bounded_host() {
    // The validator and reload steps of a config write reach the machine through
    // `ConfigHost::run`, a second entry point beside the area's own. It is
    // called through the trait explicitly because the two share a name.
    let host = ProcessFtpsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = crate::safe_write::ConfigHost::run(&host, &id, &["-u"], CallClass::Validate)
        .expect("id must run");

    assert_eq!(outcome.status, 0);
    assert!(outcome.stdout.trim().parse::<u32>().is_ok());
}
