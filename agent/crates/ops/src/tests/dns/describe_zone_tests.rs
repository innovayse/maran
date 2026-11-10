//! What the daemon says about one zone: whether it serves it and how it signs it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::dns::zone_name::ZoneName;

use super::super::dns_error::DnsError;
use super::super::fake_dns_host::distro;
use super::super::fake_zone_host::{FakeZoneHost, zone_directory};
use super::describe_zone;
use crate::dns::model::key_publication::KeyPublication;
use crate::dns::model::signing_mode::SigningMode;
use crate::dns::model::zone_description::ZoneDescription;

fn zone() -> ZoneName {
    ZoneName::parse("example.com").unwrap()
}

#[test]
fn a_served_zone_is_described_with_its_serial_and_its_observed_signing() {
    let host = FakeZoneHost::new().with_zone(&zone_directory(), "example.com", 42, "; x\n");

    let described = describe_zone(&host, distro(), &zone()).unwrap();

    match described {
        ZoneDescription::Served { serial, signing } => {
            assert_eq!(serial, 42);
            assert_eq!(signing.mode, Some(SigningMode::Unsigned));
            assert_eq!(signing.key, KeyPublication::NotPublished);
        }
        other => panic!("expected a served zone, got {other:?}"),
    }
}

#[test]
fn a_zone_the_daemon_does_not_know_has_no_signing_to_describe() {
    let host = FakeZoneHost::new();

    assert_eq!(
        describe_zone(&host, distro(), &zone()).unwrap(),
        ZoneDescription::NotServed
    );
    assert!(host.rndc_commands().iter().all(|c| c[0] == "zonestatus"));
}

#[test]
fn a_daemon_that_cannot_be_asked_is_an_error_and_not_a_not_served_zone() {
    let host = FakeZoneHost::new().with_daemon_down();

    assert!(matches!(
        describe_zone(&host, distro(), &zone()),
        Err(DnsError::ControlRefused { .. })
    ));
}

#[test]
fn a_signing_that_cannot_be_read_does_not_hide_that_the_zone_is_served() {
    let host = FakeZoneHost::new()
        .with_zone(&zone_directory(), "example.com", 5, "; x\n")
        .with_showzone_refused();

    match describe_zone(&host, distro(), &zone()).unwrap() {
        ZoneDescription::Served { serial, signing } => {
            assert_eq!(serial, 5);
            assert_eq!(signing.mode, None);
        }
        other => panic!("expected a served zone, got {other:?}"),
    }
}
