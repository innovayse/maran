//! Tests for the `port` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{Port, PortError};

#[test]
fn the_ports_a_firewall_rule_names_are_accepted() {
    for candidate in [1_u32, 22, 80, 443, 3306, 8443, 65_535] {
        let port = Port::parse(candidate).unwrap();

        assert_eq!(u32::from(port.value()), candidate);
    }
}

#[test]
fn zero_and_65536_are_refused() {
    assert_eq!(Port::parse(0), Err(PortError::Zero));
    assert_eq!(
        Port::parse(65_536),
        Err(PortError::TooLarge { value: 65_536 })
    );
}

#[test]
fn a_value_far_above_the_field_is_refused_too() {
    assert_eq!(
        Port::parse(u32::MAX),
        Err(PortError::TooLarge { value: u32::MAX })
    );
}
