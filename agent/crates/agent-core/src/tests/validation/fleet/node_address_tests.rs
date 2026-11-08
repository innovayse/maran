//! Tests for the `node_address` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::{NodeAddress, NodeAddressError};

#[test]
fn an_ipv4_and_an_ipv6_literal_are_accepted_as_ip_addresses() {
    assert_eq!(
        NodeAddress::parse("203.0.113.7").unwrap(),
        NodeAddress::Ip("203.0.113.7".parse::<IpAddr>().unwrap())
    );
    assert!(matches!(
        NodeAddress::parse("2001:db8::1").unwrap(),
        NodeAddress::Ip(_)
    ));
}

#[test]
fn a_host_name_is_accepted_and_lowercased() {
    match NodeAddress::parse("Node1.Example.COM").unwrap() {
        NodeAddress::Host(domain) => assert_eq!(domain.as_str(), "node1.example.com"),
        other => panic!("expected a host name, got {other:?}"),
    }
}

#[test]
fn a_private_network_name_is_accepted() {
    assert!(matches!(
        NodeAddress::parse("node1.lan").unwrap(),
        NodeAddress::Host(_)
    ));
}

#[test]
fn an_unspecified_or_multicast_address_is_not_dialable() {
    for candidate in ["0.0.0.0", "::", "224.0.0.1", "ff02::1"] {
        assert!(
            matches!(
                NodeAddress::parse(candidate),
                Err(NodeAddressError::NotDialable { .. })
            ),
            "{candidate:?} must be refused as not dialable"
        );
    }
}

#[test]
fn text_that_is_neither_an_address_nor_a_name_is_refused() {
    for candidate in [
        "",
        "node 1",
        "node1.example.com:8444",
        "https://node1.example.com",
        "node1.example.com\n",
        "-node.example.com",
        "999.1.1.1",
        "1.2.3",
        "12345",
    ] {
        assert!(
            matches!(
                NodeAddress::parse(candidate),
                Err(NodeAddressError::NotAnAddress { .. })
            ),
            "{candidate:?} must be refused as not an address"
        );
    }
}
