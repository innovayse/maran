//! The host that touches the machine, checked against what any Linux host has.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use crate::mail::mail_host::MailHost;
use crate::mail::process_mail_host::ProcessMailHost;

#[test]
fn a_file_that_is_not_there_reads_as_absent_and_one_that_is_reads_as_its_text() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("table");
    let host = ProcessMailHost::new();

    assert_eq!(host.read_config(&path).unwrap(), None);
    std::fs::write(&path, "a b\n").unwrap();
    assert_eq!(host.read_config(&path).unwrap().as_deref(), Some("a b\n"));
}

#[test]
fn a_path_that_exists_is_reported_and_one_that_does_not_is_not() {
    let directory = tempfile::tempdir().unwrap();
    let host = ProcessMailHost::new();

    assert!(host.path_exists(directory.path()));
    assert!(!host.path_exists(&directory.path().join("missing")));
}

#[test]
fn the_kernels_socket_table_can_be_read() {
    let sockets = ProcessMailHost::new().listening_sockets();

    assert!(sockets.is_ok(), "{sockets:?}");
}

#[test]
fn a_program_runs_under_the_class_its_caller_names() {
    let host = ProcessMailHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = MailHost::run(&host, &id, &["-u"], CallClass::Query).unwrap();

    assert_eq!(outcome.status, 0);
    assert!(outcome.stdout.trim().parse::<u32>().is_ok());
}

#[test]
fn the_host_runs_every_program_under_the_class_its_caller_names() {
    // The host owns no class of its own: a literal here would override the
    // caller's choice for every operation at once.
    let classes = crate::test_support::call_class_census::classes_named_in_code(include_str!(
        "../../mail/process_mail_host.rs"
    ));

    assert!(classes.is_empty(), "{classes:?}");
}

#[test]
fn the_monotonic_clock_does_not_run_backwards() {
    let host = ProcessMailHost::new();
    let first = host.monotonic_now();

    assert!(host.monotonic_now() >= first);
}
