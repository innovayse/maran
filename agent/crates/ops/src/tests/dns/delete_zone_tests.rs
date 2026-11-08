//! Tests for `delete_zone`.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::dns_error::DnsError;
use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, zone_directory};
use super::delete_zone;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

#[test]
fn a_served_zone_is_removed_from_the_daemon_and_its_file_is_deleted() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 3, "; x\n");

    let outcome = delete_zone(&host, distro(), &zone_directory(), &zone()).unwrap();

    assert!(outcome.was_present);
    assert!(host.served_serial("example.com").is_none());
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none()
    );
}

#[test]
fn deleting_again_succeeds_and_says_nothing_was_there() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 3, "; x\n");
    delete_zone(&host, distro(), &zone_directory(), &zone()).unwrap();

    let again = delete_zone(&host, distro(), &zone_directory(), &zone()).unwrap();

    assert!(!again.was_present);
}

#[test]
fn a_file_the_daemon_does_not_serve_is_still_removed() {
    let host = FakeZoneHost::new().with_unserved_file(&zone_directory(), "example.com");

    let outcome = delete_zone(&host, distro(), &zone_directory(), &zone()).unwrap();

    assert!(outcome.was_present);
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_none()
    );
    assert!(host.rndc_commands().iter().all(|c| c[0] != "delzone"));
}

#[test]
fn a_daemon_that_does_not_answer_leaves_the_file_where_it_is() {
    let host = FakeZoneHost::new()
        .with_unserved_file(&zone_directory(), "example.com")
        .with_daemon_down();

    let refused = delete_zone(&host, distro(), &zone_directory(), &zone());

    assert!(matches!(refused, Err(DnsError::ControlRefused { .. })));
    assert!(
        host.file(&zone_directory().join("example.com.db"))
            .is_some()
    );
}
