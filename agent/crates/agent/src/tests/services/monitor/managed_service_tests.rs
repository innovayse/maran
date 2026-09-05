//! Tests for the mapping from a status row's position to the service it names.
//!
//! Position IS the identity of a row: `DistroAdapter::managed_units` fixes the
//! set and the order, and `get_service_statuses` reports one row per unit in
//! exactly that order. These tests pin the mapping against the adapter's own
//! documented order rather than against a copy of it — an off-by-one here
//! labels the cron daemon's state as the database's, and a panel then pages an
//! operator about the wrong service.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_distro::{DistroFamily, adapter_for};

use super::managed_service;
use crate::proto::ManagedService;

#[test]
fn each_position_names_the_service_the_adapter_puts_there() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        let adapter = adapter_for(family);
        let units = adapter.managed_units();

        // The expectation comes from the adapter's OWN accessors, not from a
        // list of unit names typed here: `managed_units` is documented to be
        // exactly these four answers in this order, so a family that renamed a
        // unit in one place and not the other fails this rather than agreeing
        // with itself.
        assert_eq!(units[0], adapter.nginx_service(), "{family:?}");
        assert_eq!(units[1], adapter.mysql_service(), "{family:?}");
        assert_eq!(units[2], adapter.cron_service(), "{family:?}");
        assert_eq!(units[3], adapter.ssh_service(), "{family:?}");

        assert_eq!(managed_service(0), ManagedService::WebServer, "{family:?}");
        assert_eq!(managed_service(1), ManagedService::Database, "{family:?}");
        assert_eq!(managed_service(2), ManagedService::Cron, "{family:?}");
        assert_eq!(managed_service(3), ManagedService::Ssh, "{family:?}");
    }
}

#[test]
fn every_position_the_adapter_can_produce_is_named() {
    // The array's length is the contract, so this counts positions rather than
    // assuming four. Widening `managed_units` without extending the mapping
    // leaves the new unit reported as UNSPECIFIED, and this is what says so.
    let units = adapter_for(DistroFamily::Debian).managed_units();

    for (position, unit) in units.iter().enumerate() {
        assert_ne!(
            managed_service(position),
            ManagedService::Unspecified,
            "position {position} ({unit}) has no name on the wire"
        );
    }
}

#[test]
fn a_position_beyond_the_adapters_set_is_unspecified_rather_than_guessed() {
    // Unreachable while the array is a fixed four, and here so that widening it
    // cannot silently start labelling a new unit as an existing one. The
    // contract tells a reader what to do with UNSPECIFIED: it is not evidence
    // of health and not evidence of an outage.
    let beyond = adapter_for(DistroFamily::Debian).managed_units().len();

    assert_eq!(managed_service(beyond), ManagedService::Unspecified);
}
