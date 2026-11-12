//! Tests for [`read_host_addresses`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::read_host_addresses;

#[test]
fn this_hosts_own_addresses_include_the_loopback() {
    let addresses = read_host_addresses().unwrap().addresses;

    assert!(
        addresses.contains(&"127.0.0.1".parse::<IpAddr>().unwrap()),
        "the kernel table was not read: {addresses:?}"
    );
}

#[test]
fn an_address_no_interface_carries_is_not_listed() {
    // The inverse control: the answer is the host's list, not every address.
    let addresses = read_host_addresses().unwrap().addresses;

    assert!(!addresses.contains(&"192.0.2.77".parse::<IpAddr>().unwrap()));
}
