//! Tests for `get_server_fingerprint_inputs`: reading both fingerprint
//! inputs through the injectable host, rather than the pure parsers
//! `machine_identity_tests.rs` and `primary_interface_tests.rs` already
//! cover.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::get_server_fingerprint_inputs;
use crate::monitor::fake_monitor_host::FakeMonitorHost;
use crate::monitor::{MachineIdentity, MonitorError, PrimaryInterface};
use maran_distro::DistroAdapter;
use maran_distro::debian::DebianAdapter;

#[test]
fn both_values_present_are_both_reported() {
    let host = FakeMonitorHost::from_ubuntu_captures()
        .with_machine_id("4e3ff4943c924fe4ab28141e8bebb6a3")
        .with_ipv4_routes(
            "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n\
eth0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n",
        );

    let inputs =
        get_server_fingerprint_inputs(&host, &DebianAdapter).expect("both files are readable");

    assert_eq!(
        inputs.machine_id,
        MachineIdentity::Present("4e3ff4943c924fe4ab28141e8bebb6a3".to_owned())
    );
    assert_eq!(
        inputs.primary_interface,
        PrimaryInterface::Present("eth0".to_owned())
    );
}

/// An absent `machine-id` is not an error, and must not stop the primary
/// interface from being reported — the two inputs are independent.
#[test]
fn an_absent_machine_id_still_reports_the_interface() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_absent_machine_id();

    let inputs = get_server_fingerprint_inputs(&host, &DebianAdapter)
        .expect("the routing table is readable");

    assert_eq!(inputs.machine_id, MachineIdentity::NotAvailable);
    assert_ne!(inputs.primary_interface, PrimaryInterface::NotAvailable);
}

/// No default route in the table is not an error either.
#[test]
fn no_default_route_still_reports_the_machine_id() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_ipv4_routes(
        "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n",
    );

    let inputs =
        get_server_fingerprint_inputs(&host, &DebianAdapter).expect("the machine-id is readable");

    assert_eq!(inputs.primary_interface, PrimaryInterface::NotAvailable);
    assert_ne!(inputs.machine_id, MachineIdentity::NotAvailable);
}

/// A genuine failure to read `/etc/machine-id` (not just its absence) is
/// surfaced as an error and stops the call — this is a failure to observe,
/// not a finding.
#[test]
fn an_unreadable_machine_id_is_an_error() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_machine_id();

    let result = get_server_fingerprint_inputs(&host, &DebianAdapter);

    assert_eq!(result, Err(MonitorError::MachineIdUnavailable));
}

#[test]
fn an_unreadable_routing_table_is_an_error() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_unreadable_ipv4_routes();

    let result = get_server_fingerprint_inputs(&host, &DebianAdapter);

    assert_eq!(result, Err(MonitorError::Ipv4RoutesUnavailable));
}

/// The operation reads the path the adapter names, not one of its own.
///
/// The VALUE is asserted, not merely that something was read. `rules/architecture.md` forbids a
/// platform literal in `ops` precisely so the two families can diverge one day by changing one
/// arm; a check that only saw "a path was requested" would pass on a hard-coded `/etc/machine-id`
/// and the rule would be unenforced in the one place it matters.
///
/// This suite has been caught by exactly that shape before: the sshd-jail test called itself a
/// read-your-own-answer control while its fake discarded the path argument, so the property it
/// named was never observed. The fake records the path for this reason.
#[test]
fn the_operation_reads_the_path_the_adapter_names() {
    let host = FakeMonitorHost::from_ubuntu_captures();

    let _ = get_server_fingerprint_inputs(&host, &DebianAdapter);

    assert_eq!(
        host.machine_id_path_requested(),
        Some(DebianAdapter.machine_id_path().to_owned())
    );
}
