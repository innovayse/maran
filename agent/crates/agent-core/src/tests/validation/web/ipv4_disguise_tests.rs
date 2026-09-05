//! Tests for the `ipv4_disguise` module.
//!
//! Both directions, because the predicate has two jobs and they pull against
//! each other: it must catch every IPv4 address wearing IPv6 notation, and it
//! must not catch the two ordinary IPv6 addresses that share the notation's
//! shape.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::{Ipv4Addr, Ipv6Addr};

use super::ipv4_in_disguise;

/// Parses a v6 literal for the table below.
fn v6(text: &str) -> Ipv6Addr {
    text.parse().unwrap()
}

/// Parses a v4 literal for the table below.
fn v4(text: &str) -> Ipv4Addr {
    text.parse().unwrap()
}

#[test]
fn an_ipv4_address_in_ipv6_notation_is_seen_through() {
    // The v4-mapped form, which is the one anyone actually writes.
    assert_eq!(ipv4_in_disguise(v6("::ffff:1.2.3.4")), Some(v4("1.2.3.4")));
    assert_eq!(
        ipv4_in_disguise(v6("::ffff:203.0.113.7")),
        Some(v4("203.0.113.7"))
    );

    // The v4-compatible form, deprecated by RFC 4291 — `std` parses it and
    // renders it back as `::102:304`, so both spellings reach this predicate.
    assert_eq!(ipv4_in_disguise(v6("::1.2.3.4")), Some(v4("1.2.3.4")));
    assert_eq!(ipv4_in_disguise(v6("::102:304")), Some(v4("1.2.3.4")));
}

#[test]
fn the_unspecified_address_and_loopback_are_ordinary_ipv6_addresses() {
    // Both have the ninety-six leading zero bits the v4-compatible form is
    // defined by, so `Ipv6Addr::to_ipv4` answers `Some` for them — which is why
    // they are carved out here rather than left to it.
    assert_eq!(ipv4_in_disguise(v6("::")), None);
    assert_eq!(ipv4_in_disguise(v6("::1")), None);
}

#[test]
fn a_real_ipv6_address_is_left_alone() {
    for text in [
        "2001:db8::1",
        "2001:db8::",
        "fe80::1",
        "fc00::1",
        "64:ff9b::102:304",
        "::ffff:0:102:304",
    ] {
        assert_eq!(
            ipv4_in_disguise(v6(text)),
            None,
            "`{text}` is an IPv6 address and must be treated as one"
        );
    }
}
