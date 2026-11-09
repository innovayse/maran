//! Tests for `get_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::dns_error::DnsError;
use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, zone_directory};
use super::super::model::zone_reading::ZoneReading;
use super::get_zone;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

#[test]
fn a_served_zone_reports_the_serial_the_daemon_holds() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 42, "; x\n");

    assert_eq!(
        get_zone(&host, distro(), &zone()).unwrap(),
        ZoneReading::Served { serial: 42 }
    );
}

#[test]
fn a_zone_the_daemon_does_not_know_is_not_served() {
    let host = FakeZoneHost::new();

    assert_eq!(
        get_zone(&host, distro(), &zone()).unwrap(),
        ZoneReading::NotServed
    );
}

#[test]
fn a_file_on_disk_does_not_make_a_zone_served() {
    let host = FakeZoneHost::new().with_unserved_file(&zone_directory(), "example.com");

    assert_eq!(
        get_zone(&host, distro(), &zone()).unwrap(),
        ZoneReading::NotServed
    );
}

#[test]
fn a_daemon_that_is_down_is_an_error_and_not_a_zone_that_is_missing() {
    let host = FakeZoneHost::new().with_daemon_down();

    assert!(matches!(
        get_zone(&host, distro(), &zone()),
        Err(DnsError::ControlRefused { .. })
    ));
}
