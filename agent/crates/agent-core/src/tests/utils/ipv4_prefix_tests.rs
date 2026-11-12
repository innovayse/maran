//! Tests for [`Ipv4Prefix`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::Ipv4Addr;

use super::Ipv4Prefix;

fn prefix(text: &str, length: u8) -> Ipv4Prefix {
    Ipv4Prefix::new(text.parse().unwrap(), length).unwrap()
}

#[test]
fn host_bits_are_cleared_so_one_network_is_one_value() {
    assert_eq!(prefix("192.0.2.77", 24), prefix("192.0.2.0", 24));
    assert_eq!(prefix("192.0.2.77", 24).to_string(), "192.0.2.0/24");
}

#[test]
fn a_length_over_32_is_refused() {
    assert!(Ipv4Prefix::new(Ipv4Addr::new(192, 0, 2, 0), 33).is_none());
    assert!(Ipv4Prefix::new(Ipv4Addr::new(192, 0, 2, 0), 32).is_some());
}

#[test]
fn contains_is_true_inside_and_false_outside() {
    let range = prefix("192.0.2.0", 24);

    assert!(range.contains("192.0.2.255".parse().unwrap()));
    assert!(!range.contains("192.0.3.0".parse().unwrap()));
}

#[test]
fn the_empty_prefix_contains_everything() {
    assert!(prefix("0.0.0.0", 0).contains("203.0.113.9".parse().unwrap()));
}

#[test]
fn a_range_covers_a_narrower_one_inside_it_and_itself_but_not_the_reverse() {
    let wide = prefix("10.0.0.0", 16);
    let narrow = prefix("10.0.5.0", 24);

    assert!(wide.covers(&narrow));
    assert!(wide.covers(&wide));
    assert!(!narrow.covers(&wide));
    assert!(!prefix("10.1.0.0", 24).covers(&narrow));
}
