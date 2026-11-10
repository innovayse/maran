//! Tests for `list_zones`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, directories, zone_directory};
use super::list_zones;

#[test]
fn only_the_zones_the_daemon_serves_are_listed() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 1, "; x\n")
        .with_unserved_file(&zone_directory(), "unserved.example");

    let zones = list_zones(&host, distro(), &directories()).unwrap();

    let names: Vec<&str> = zones.iter().map(|z| z.as_str()).collect();
    assert_eq!(names, vec!["example.com"]);
}

#[test]
fn a_host_with_no_zones_lists_none() {
    let host = FakeZoneHost::new();

    assert!(
        list_zones(&host, distro(), &directories())
            .unwrap()
            .is_empty()
    );
}
