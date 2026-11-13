//! The contents of the output chain's two address sets.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;

use crate::firewall::model::self_address_sets::SelfAddressSets;

fn host(addresses: &[&str], ranges: &[(&str, u8)]) -> HostAddresses {
    HostAddresses {
        addresses: addresses.iter().map(|text| text.parse().unwrap()).collect(),
        local_prefixes: ranges
            .iter()
            .map(|(network, length)| Ipv4Prefix::new(network.parse().unwrap(), *length).unwrap())
            .collect(),
    }
}

fn spelled<T: ToString>(items: &[T]) -> Vec<String> {
    items.iter().map(ToString::to_string).collect()
}

/// A routed block is kept whole and swallows the addresses inside it: an interval
/// set refuses overlapping elements, so keeping both would fail the whole load.
#[test]
fn a_local_range_is_kept_and_the_addresses_inside_it_are_dropped() {
    let sets = SelfAddressSets::new(&host(
        &["192.0.2.10", "203.0.113.5"],
        &[("203.0.113.0", 24)],
    ));

    assert_eq!(spelled(sets.v4_prefixes()), ["203.0.113.0/24"]);
    assert_eq!(spelled(sets.v4()), ["192.0.2.10"]);
}

/// A narrower range inside a wider one is dropped, whatever order they came in.
#[test]
fn a_range_inside_another_range_is_dropped() {
    let sets = SelfAddressSets::new(&host(&[], &[("10.0.5.0", 24), ("10.0.0.0", 16)]));

    assert_eq!(spelled(sets.v4_prefixes()), ["10.0.0.0/16"]);
}

/// Inverse control: two disjoint ranges are both kept.
#[test]
fn disjoint_ranges_are_both_kept() {
    let sets = SelfAddressSets::new(&host(&[], &[("198.51.100.0", 24), ("203.0.113.0", 24)]));

    assert_eq!(
        spelled(sets.v4_prefixes()),
        ["198.51.100.0/24", "203.0.113.0/24"]
    );
}

/// The loopback range the kernel lists is the templates' constant, and a range that
/// would contain it cannot sit beside the constant.
#[test]
fn ranges_overlapping_the_loopback_constant_are_dropped() {
    let sets = SelfAddressSets::new(&host(
        &["127.0.0.1", "::1"],
        &[("127.0.0.0", 8), ("0.0.0.0", 1)],
    ));

    assert!(sets.v4_prefixes().is_empty());
    assert!(sets.v4().is_empty());
    assert!(sets.v6().is_empty());
}

/// Inverse control for the shared loopback filter: a range that merely sits next to the
/// loopback network is kept.
#[test]
fn a_range_beside_the_loopback_network_is_kept() {
    let sets = SelfAddressSets::new(&host(&[], &[("128.0.0.0", 8), ("126.0.0.0", 8)]));

    assert_eq!(spelled(sets.v4_prefixes()), ["126.0.0.0/8", "128.0.0.0/8"]);
}

#[test]
fn the_same_host_in_another_order_gives_the_same_sets() {
    let one = SelfAddressSets::new(&host(
        &["198.51.100.7", "192.0.2.10", "2001:db8::2", "2001:db8::1"],
        &[],
    ));
    let other = SelfAddressSets::new(&host(
        &[
            "192.0.2.10",
            "2001:db8::1",
            "198.51.100.7",
            "2001:db8::2",
            "192.0.2.10",
        ],
        &[],
    ));

    assert_eq!(one, other);
}
