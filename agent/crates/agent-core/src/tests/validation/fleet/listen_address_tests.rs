//! Tests for the `listen_address` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{ListenAddress, ListenAddressError};

#[test]
fn an_ipv4_address_and_an_unprivileged_port_are_accepted() {
    let address = ListenAddress::parse("0.0.0.0:8444").unwrap();

    assert_eq!(address.socket_address().port(), 8444);
    assert!(address.socket_address().ip().is_unspecified());
}

#[test]
fn a_bracketed_ipv6_address_is_accepted() {
    let address = ListenAddress::parse("[::1]:8444").unwrap();

    assert!(address.socket_address().is_ipv6());
}

#[test]
fn the_lowest_and_highest_allowed_ports_are_accepted() {
    assert!(ListenAddress::parse("127.0.0.1:1024").is_ok());
    assert!(ListenAddress::parse("127.0.0.1:65535").is_ok());
}

#[test]
fn port_zero_and_privileged_ports_are_refused() {
    assert_eq!(
        ListenAddress::parse("127.0.0.1:0"),
        Err(ListenAddressError::PortNotAllowed { port: 0 })
    );
    assert_eq!(
        ListenAddress::parse("127.0.0.1:1023"),
        Err(ListenAddressError::PortNotAllowed { port: 1023 })
    );
    assert_eq!(
        ListenAddress::parse("0.0.0.0:443"),
        Err(ListenAddressError::PortNotAllowed { port: 443 })
    );
}

#[test]
fn a_host_name_a_bare_port_and_a_port_that_does_not_fit_are_refused() {
    for candidate in [
        "localhost:8444",
        "8444",
        "10.0.0.1",
        "10.0.0.1:65536",
        "::1:8444",
        "",
        "10.0.0.1:8444\n",
    ] {
        assert!(
            matches!(
                ListenAddress::parse(candidate),
                Err(ListenAddressError::NotASocketAddress { .. })
            ),
            "{candidate:?} must be refused as not an ip:port address"
        );
    }
}
