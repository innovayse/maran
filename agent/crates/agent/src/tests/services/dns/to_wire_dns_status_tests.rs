//! A DNS observation onto its wire payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::dns::{DnsPolicyObservation, DnsState};

use crate::proto::DnsPolicyObservation as Wire;
use crate::services::dns::to_wire_dns_status::to_wire_dns_status;

/// A state with every flag set, to be varied by a test.
fn state() -> DnsState {
    DnsState {
        running: true,
        control_channel_answered: true,
        port_53_answering: true,
        recursion_refused: DnsPolicyObservation::InForce,
        response_rate_limiting: DnsPolicyObservation::InForce,
        enabled_at_boot: true,
    }
}

#[test]
fn every_flag_of_the_observation_reaches_the_wire() {
    let wire = to_wire_dns_status(&state());

    assert!(wire.running);
    assert!(wire.control_channel_answered);
    assert!(wire.port_53_answering);
    assert!(wire.enabled_at_boot);
    assert_eq!(wire.recursion_refused, Wire::InForce as i32);
    assert_eq!(wire.response_rate_limiting, Wire::InForce as i32);
}

#[test]
fn an_unreadable_configuration_is_not_observed_and_never_certified_or_accused() {
    let wire = to_wire_dns_status(&DnsState {
        recursion_refused: DnsPolicyObservation::NotObserved,
        response_rate_limiting: DnsPolicyObservation::NotObserved,
        ..state()
    });

    assert_eq!(wire.recursion_refused, Wire::NotObserved as i32);
    assert_eq!(wire.response_rate_limiting, Wire::NotObserved as i32);
}

#[test]
fn a_configuration_that_allows_recursion_is_not_in_force_and_not_unobserved() {
    let wire = to_wire_dns_status(&DnsState {
        recursion_refused: DnsPolicyObservation::NotInForce,
        ..state()
    });

    assert_eq!(wire.recursion_refused, Wire::NotInForce as i32);
}

#[test]
fn the_two_flags_that_can_differ_are_carried_apart() {
    let wire = to_wire_dns_status(&DnsState {
        running: true,
        control_channel_answered: false,
        port_53_answering: false,
        ..state()
    });

    assert!(wire.running);
    assert!(!wire.control_channel_answered);
    assert!(!wire.port_53_answering);
}
