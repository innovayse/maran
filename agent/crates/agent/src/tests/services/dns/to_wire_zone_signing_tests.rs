//! A zone's observed signing onto its wire payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::dns::{KeyPublication, SigningMode, ZoneSigningObservation};

use crate::proto::{DnsPolicyObservation as Wire, DnsSigningMode};
use crate::services::dns::to_wire_zone_signing::to_wire_zone_signing;

#[test]
fn an_observed_signed_zone_with_a_published_key_carries_both_facts() {
    let wire = to_wire_zone_signing(&ZoneSigningObservation {
        mode: Some(SigningMode::Signed),
        key: KeyPublication::Published(59_409),
    });

    assert_eq!(wire.mode, DnsSigningMode::Signed as i32);
    assert_eq!(wire.mode_observed, Wire::InForce as i32);
    assert_eq!(wire.published_key_tag, 59_409);
    assert_eq!(wire.published_key_observed, Wire::InForce as i32);
}

#[test]
fn every_mode_reaches_the_wire_as_itself() {
    for (mode, expected) in [
        (SigningMode::Unsigned, DnsSigningMode::Unsigned),
        (SigningMode::Signed, DnsSigningMode::Signed),
        (SigningMode::Withdrawing, DnsSigningMode::Withdrawing),
    ] {
        let wire = to_wire_zone_signing(&ZoneSigningObservation {
            mode: Some(mode),
            key: KeyPublication::NotPublished,
        });

        assert_eq!(wire.mode, expected as i32);
        assert_eq!(wire.mode_observed, Wire::InForce as i32);
    }
}

#[test]
fn nothing_observed_is_not_observed_and_never_unsigned_in_force() {
    let wire = to_wire_zone_signing(&ZoneSigningObservation::NOT_OBSERVED);

    assert_eq!(wire.mode_observed, Wire::NotObserved as i32);
    assert_eq!(wire.published_key_observed, Wire::NotObserved as i32);
    assert_eq!(wire.published_key_tag, 0);
}

#[test]
fn a_key_the_daemon_reports_none_of_is_not_in_force_and_not_unobserved() {
    let wire = to_wire_zone_signing(&ZoneSigningObservation {
        mode: Some(SigningMode::Signed),
        key: KeyPublication::NotPublished,
    });

    assert_eq!(wire.published_key_observed, Wire::NotInForce as i32);
    assert_eq!(wire.published_key_tag, 0);
}
