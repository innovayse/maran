//! The real DNS host starting a real program through the bounded core, and the
//! classes the operations choose for their spawns.
//!
//! The program is a real adapter's `id`: an `AdapterBinary` cannot be built
//! outside the distro crate, and `id` exists at one path on every host this
//! suite runs on and needs no privilege.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_distro::{DistroFamily, adapter_for};

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::dns::process_dns_host::ProcessDnsHost;
use crate::safe_write::model::Validator;
use crate::test_support::call_class_census::classes_named_in_code;

#[test]
fn a_program_the_adapter_names_is_run_and_its_output_comes_back() {
    let host = ProcessDnsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();

    let outcome = DnsHost::run(&host, &id, &["-u"], CallClass::Query).unwrap();

    assert_eq!(outcome.status, 0);
    assert!(!outcome.stdout.trim().is_empty());
}

#[test]
fn a_file_that_is_not_there_reads_as_absent_and_not_as_an_error() {
    let host = ProcessDnsHost::new();

    assert_eq!(
        host.read_config(Path::new("/nonexistent/maran-dns/named.conf"))
            .unwrap(),
        None
    );
}

#[test]
fn removing_a_file_that_is_not_there_is_success() {
    let host = ProcessDnsHost::new();

    host.remove_file(Path::new("/nonexistent/maran-dns/named.conf"))
        .unwrap();
}

#[test]
fn a_port_nothing_listens_on_does_not_accept() {
    let host = ProcessDnsHost::new();
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    drop(listener);

    assert!(!host.tcp_port_accepts(port));
}

#[test]
fn a_port_with_a_listener_accepts() {
    // The inverse control of the test above: the probe can see a listener at all.
    let host = ProcessDnsHost::new();
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();

    assert!(host.tcp_port_accepts(port));
}

#[test]
fn the_host_runs_every_program_under_the_class_its_caller_names() {
    // The host owns no class of its own: a literal here would override the
    // caller's choice for every operation at once.
    let classes = classes_named_in_code(include_str!("../../dns/process_dns_host.rs"));

    assert!(classes.is_empty(), "{classes:?}");
}

#[test]
fn the_operations_pin_the_classes_their_spawns_run_under() {
    // A deadline is the one thing no behaviour test waits long enough to observe.
    // Questions are Query, the checker is Validate, a service-manager verb that
    // changes state is Reload.
    let status = classes_named_in_code(include_str!("../../dns/get_dns_status.rs"));
    let wait = classes_named_in_code(include_str!("../../dns/wait_for_control_channel.rs"));
    let disable = classes_named_in_code(include_str!("../../dns/disable_dns.rs"));
    let enable = classes_named_in_code(include_str!("../../dns/enable_dns.rs"));

    assert_eq!(status, ["Query", "Query", "Query"]);
    assert_eq!(wait, ["Query"]);
    assert_eq!(disable, ["Reload"]);
    assert_eq!(
        enable,
        ["Validate", "Reload", "Reload", "Validate", "Reload"]
    );
}

#[test]
fn a_probe_the_validator_accepts_is_answered_ok_and_leaves_no_file() {
    let host = ProcessDnsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("dnssec-probe.conf");

    // `id -u` exits 0: a validator that accepts.
    host.probe_config(
        &target,
        "probe\n",
        &Validator {
            program: &id,
            arguments: &["-u"],
            class: CallClass::Query,
        },
    )
    .unwrap();

    assert!(!target.exists(), "the probe file outlived the question");
}

#[test]
fn a_probe_the_validator_refuses_is_an_error_and_leaves_no_file() {
    // The planted refusal: `id` of a user nobody has exits non-zero.
    let host = ProcessDnsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("dnssec-probe.conf");

    let refused = host.probe_config(
        &target,
        "probe\n",
        &Validator {
            program: &id,
            arguments: &["--", "no-such-user-for-maran-probe"],
            class: CallClass::Query,
        },
    );

    assert!(matches!(refused, Err(DnsError::ConfigRejected { .. })));
    assert!(!target.exists(), "the refused probe file stayed");
}

#[test]
fn a_leftover_probe_from_an_interrupted_run_is_removed_by_the_next_one() {
    let host = ProcessDnsHost::new();
    let id = adapter_for(DistroFamily::Debian).id_binary();
    let directory = tempfile::tempdir().unwrap();
    let target = directory.path().join("dnssec-probe.conf");
    std::fs::write(&target, "left over\n").unwrap();

    let refused = host.probe_config(
        &target,
        "probe\n",
        &Validator {
            program: &id,
            arguments: &["--", "no-such-user-for-maran-probe"],
            class: CallClass::Query,
        },
    );

    assert!(refused.is_err());
    assert!(!target.exists());
}

#[test]
fn the_new_operations_pin_the_classes_their_spawns_run_under() {
    let probe = classes_named_in_code(include_str!("../../dns/probe_dnssec_support.rs"));
    let mode = classes_named_in_code(include_str!("../../dns/read_zone_mode.rs"));
    let state = classes_named_in_code(include_str!("../../dns/read_signing_state.rs"));
    let put = classes_named_in_code(include_str!("../../dns/put_zone.rs"));

    assert_eq!(probe, ["Validate"]);
    assert_eq!(mode, ["Query"]);
    assert_eq!(state, ["Query"]);
    assert_eq!(put, ["Validate"]);
}
