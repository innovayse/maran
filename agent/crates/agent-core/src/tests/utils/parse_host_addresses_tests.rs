//! Tests for [`parse_host_addresses`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::{ADDRESS_TABLE_LIMIT, parse_host_addresses};
use crate::utils::host_addresses_error::HostAddressesError;

const FIB_TRIE: &str = "Local:\n     |-- 203.0.113.7\n        /32 host LOCAL\n     |-- 127.0.0.1\n        /32 host LOCAL\n";
const IF_INET6: &str = "20010db8000000000000000000000001 02 40 00 80 eth0\n";

#[test]
fn both_tables_are_listed_ipv4_first() {
    let addresses = parse_host_addresses(Some(FIB_TRIE), Some(IF_INET6))
        .unwrap()
        .addresses;

    assert_eq!(
        addresses,
        [
            "203.0.113.7".parse::<IpAddr>().unwrap(),
            "127.0.0.1".parse().unwrap(),
            "2001:db8::1".parse().unwrap(),
        ]
    );
}

/// An absent IPv6 table is IPv6 turned off; it lists nothing and is not a fault.
#[test]
fn an_absent_ipv6_table_lists_nothing() {
    let addresses = parse_host_addresses(Some(FIB_TRIE), None)
        .unwrap()
        .addresses;

    assert_eq!(addresses.len(), 2);
}

/// A missing IPv4 table is refused: every host has a loopback, so it is a fault.
#[test]
fn a_missing_ipv4_table_is_refused_not_read_as_an_empty_host() {
    assert_eq!(
        parse_host_addresses(None, Some(IF_INET6)),
        Err(HostAddressesError::Ipv4Missing)
    );
}

#[test]
fn a_table_over_the_bound_is_refused() {
    let huge = "x".repeat(ADDRESS_TABLE_LIMIT + 1);

    assert_eq!(
        parse_host_addresses(Some(&huge), None),
        Err(HostAddressesError::Ipv4OverBound)
    );
    assert_eq!(
        parse_host_addresses(Some(FIB_TRIE), Some(&huge)),
        Err(HostAddressesError::Ipv6OverBound)
    );
}

/// Inverse control: a table exactly at the bound is read.
#[test]
fn a_table_at_the_bound_is_read() {
    let padded = format!(
        "{FIB_TRIE}{}",
        " ".repeat(ADDRESS_TABLE_LIMIT - FIB_TRIE.len())
    );

    assert_eq!(padded.len(), ADDRESS_TABLE_LIMIT);
    assert_eq!(
        parse_host_addresses(Some(&padded), None)
            .unwrap()
            .addresses
            .len(),
        2
    );
}

/// The routing table's local ranges ride with the addresses, and a host with none
/// lists none (inverse control).
#[test]
fn local_ranges_are_listed_beside_the_addresses() {
    let with_range = format!("{FIB_TRIE}     |-- 192.0.2.0\n        /24 universe LOCAL\n");

    let host = parse_host_addresses(Some(&with_range), None).unwrap();
    assert_eq!(host.local_prefixes.len(), 1);
    assert_eq!(host.local_prefixes[0].to_string(), "192.0.2.0/24");

    let plain = parse_host_addresses(Some(FIB_TRIE), None).unwrap();
    assert!(plain.local_prefixes.is_empty());
}
