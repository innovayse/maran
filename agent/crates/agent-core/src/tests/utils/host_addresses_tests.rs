//! Tests for [`HostAddresses`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::HostAddresses;
use crate::utils::ipv4_prefix::Ipv4Prefix;

fn address(text: &str) -> IpAddr {
    text.parse().unwrap()
}

#[test]
fn two_readings_in_another_order_normalize_to_the_same_value() {
    let one = HostAddresses {
        addresses: vec![
            address("192.0.2.1"),
            address("198.51.100.2"),
            address("192.0.2.1"),
        ],
        local_prefixes: vec![Ipv4Prefix::new("203.0.113.0".parse().unwrap(), 24).unwrap()],
    };
    let other = HostAddresses {
        addresses: vec![address("198.51.100.2"), address("192.0.2.1")],
        local_prefixes: vec![Ipv4Prefix::new("203.0.113.9".parse().unwrap(), 24).unwrap()],
    };

    assert_ne!(one, other);
    assert_eq!(one.normalized(), other.normalized());
}

/// Inverse control: a really different host does not normalize equal.
#[test]
fn a_gained_address_is_a_different_value() {
    let before = HostAddresses {
        addresses: vec![address("192.0.2.1")],
        local_prefixes: Vec::new(),
    };
    let after = HostAddresses {
        addresses: vec![address("192.0.2.1"), address("192.0.2.2")],
        local_prefixes: Vec::new(),
    };

    assert_ne!(before.normalized(), after.normalized());
}

fn prefix(text: &str, length: u8) -> Ipv4Prefix {
    Ipv4Prefix::new(text.parse().unwrap(), length).unwrap()
}

/// The shared filter drops a range inside the loopback network AND one around it, and
/// keeps an ordinary range, sorted and once.
#[test]
fn ranges_touching_the_loopback_network_are_dropped_and_the_rest_sorted() {
    let host = HostAddresses {
        addresses: Vec::new(),
        local_prefixes: vec![
            prefix("203.0.113.0", 24),
            prefix("127.0.0.0", 8),
            prefix("127.1.0.0", 16),
            prefix("0.0.0.0", 0),
            prefix("126.0.0.0", 7),
            prefix("198.51.100.0", 24),
            prefix("203.0.113.0", 24),
        ],
    };

    assert_eq!(
        host.ranges_beside_loopback(),
        vec![prefix("198.51.100.0", 24), prefix("203.0.113.0", 24)]
    );
}

/// Inverse control: a range next to the loopback network (not inside, not around) stays.
#[test]
fn a_range_beside_the_loopback_network_is_kept() {
    let host = HostAddresses {
        addresses: Vec::new(),
        local_prefixes: vec![prefix("128.0.0.0", 8), prefix("126.0.0.0", 8)],
    };

    assert_eq!(host.ranges_beside_loopback().len(), 2);
}

#[test]
fn loopback_and_unspecified_addresses_are_dropped_and_the_others_kept() {
    let host = HostAddresses {
        addresses: vec![
            address("127.0.0.1"),
            address("::1"),
            address("0.0.0.0"),
            address("::"),
            address("192.0.2.1"),
            address("2001:db8::1"),
        ],
        local_prefixes: Vec::new(),
    };

    assert_eq!(
        host.addresses_beside_loopback().collect::<Vec<_>>(),
        vec![address("192.0.2.1"), address("2001:db8::1")]
    );
}
