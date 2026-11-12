//! Tests for [`choose_source_address`]: the policy is applied to the RESOLVED
//! address, every address of a name must pass, and the first one is used.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::{choose_source_address, embedded_ipv4, is_refused};
use crate::transfer::fake_transfer_host::FakeTransferHost;
use crate::transfer::transfer_fixtures::endpoint_at;
use crate::transfer::transfer_op_error::TransferOpError;

/// The address of `text`.
fn address(text: &str) -> IpAddr {
    text.parse().unwrap()
}

/// Whether a name that resolves only to `resolved` is refused.
fn name_resolving_to_is_refused(resolved: &str) -> bool {
    let host = FakeTransferHost::new()
        .owning(&["127.0.0.1", "203.0.113.7", "2001:db8::7"])
        .resolving("source.example.org", &[resolved]);

    matches!(
        choose_source_address(&host, &endpoint_at("source.example.org")),
        Err(TransferOpError::AddressRefused)
    )
}

#[test]
fn a_public_looking_name_that_resolves_to_a_forbidden_address_is_refused() {
    for resolved in [
        "127.0.0.1",
        "127.8.9.10",
        "0.0.0.0",
        "0.1.2.3",
        "169.254.169.254",
        "224.0.0.1",
        "239.255.255.250",
        "255.255.255.255",
        "::1",
        "::",
        "fe80::1",
        "ff02::1",
        "::ffff:127.0.0.1",
        "::ffff:169.254.169.254",
        "::127.0.0.1",
        "64:ff9b::7f00:1",
    ] {
        assert!(
            name_resolving_to_is_refused(resolved),
            "{resolved} was allowed"
        );
    }
}

#[test]
fn a_name_that_resolves_to_one_of_this_hosts_own_addresses_is_refused() {
    assert!(name_resolving_to_is_refused("203.0.113.7"));
    assert!(name_resolving_to_is_refused("2001:db8::7"));
    assert!(
        name_resolving_to_is_refused("::ffff:203.0.113.7"),
        "the mapped form of an own address got through"
    );
}

#[test]
fn public_and_private_addresses_are_allowed() {
    for resolved in [
        "198.51.100.20",
        "10.1.2.3",
        "192.168.0.9",
        "172.16.5.5",
        "2001:db8::99",
    ] {
        assert!(
            !name_resolving_to_is_refused(resolved),
            "{resolved} was refused"
        );
    }
}

#[test]
fn the_first_resolved_address_is_returned() {
    let host = FakeTransferHost::new()
        .resolving("source.example.org", &["198.51.100.20", "198.51.100.21"]);

    assert_eq!(
        choose_source_address(&host, &endpoint_at("source.example.org")).unwrap(),
        address("198.51.100.20")
    );
}

#[test]
fn one_forbidden_record_among_allowed_ones_refuses_the_name() {
    let host =
        FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20", "127.0.0.1"]);

    assert!(matches!(
        choose_source_address(&host, &endpoint_at("source.example.org")),
        Err(TransferOpError::AddressRefused)
    ));
}

#[test]
fn the_name_is_resolved_exactly_once() {
    let host = FakeTransferHost::new().resolving("source.example.org", &["198.51.100.20"]);

    choose_source_address(&host, &endpoint_at("source.example.org")).unwrap();

    assert_eq!(host.resolutions(), 1);
}

#[test]
fn a_literal_is_used_as_it_is_and_never_resolved() {
    let host = FakeTransferHost::new();

    assert_eq!(
        choose_source_address(&host, &endpoint_at("198.51.100.20")).unwrap(),
        address("198.51.100.20")
    );
    assert_eq!(host.resolutions(), 0);
}

#[test]
fn a_literal_that_is_one_of_this_hosts_own_addresses_is_refused() {
    let host = FakeTransferHost::new().owning(&["198.51.100.20"]);

    assert!(matches!(
        choose_source_address(&host, &endpoint_at("198.51.100.20")),
        Err(TransferOpError::AddressRefused)
    ));
}

#[test]
fn a_name_that_does_not_resolve_is_unreachable() {
    let host = FakeTransferHost::new();

    assert!(matches!(
        choose_source_address(&host, &endpoint_at("nowhere.example.org")),
        Err(TransferOpError::Unreachable)
    ));
}

#[test]
fn a_host_that_cannot_list_its_own_addresses_fails_closed() {
    let host = FakeTransferHost::new()
        .without_address_table()
        .resolving("source.example.org", &["198.51.100.20"]);

    assert!(matches!(
        choose_source_address(&host, &endpoint_at("source.example.org")),
        Err(TransferOpError::HostAddressesUnavailable(_))
    ));
}

#[test]
fn an_embedded_ipv4_address_is_found_in_the_three_embedding_forms() {
    for (form, inner) in [
        ("::ffff:10.1.2.3", "10.1.2.3"),
        ("::10.1.2.3", "10.1.2.3"),
        ("64:ff9b::a01:203", "10.1.2.3"),
    ] {
        let IpAddr::V6(v6) = address(form) else {
            panic!("{form} is not IPv6");
        };
        assert_eq!(embedded_ipv4(&v6), Some(inner.parse().unwrap()), "{form}");
    }
}

#[test]
fn ordinary_ipv6_addresses_embed_nothing() {
    for form in ["2001:db8::1", "::1", "::"] {
        let IpAddr::V6(v6) = address(form) else {
            panic!("{form} is not IPv6");
        };
        assert_eq!(embedded_ipv4(&v6), None, "{form}");
    }
}

#[test]
fn the_check_itself_accepts_an_allowed_address_and_refuses_a_forbidden_one() {
    assert!(!is_refused(&address("198.51.100.20"), &[]));
    assert!(is_refused(&address("127.0.0.1"), &[]));
}
