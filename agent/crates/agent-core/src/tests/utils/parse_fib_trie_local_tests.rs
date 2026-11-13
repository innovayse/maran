//! Tests for [`parse_fib_trie_local`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::Ipv4Addr;

use super::parse_fib_trie_local;

/// A trimmed table in the kernel's format: two interface addresses, the
/// loopback, a broadcast leaf and a network that is `LOCAL` as a whole.
const TABLE: &str = "Main:
  +-- 0.0.0.0/0 3 0 5
     |-- 0.0.0.0
        /0 universe UNICAST
     +-- 10.0.0.0/24 2 0 2
        |-- 10.0.0.0
           /32 link BROADCAST
           /24 link UNICAST
        |-- 10.0.0.5
           /32 host LOCAL
        |-- 10.0.0.255
           /32 link BROADCAST
     +-- 127.0.0.0/8 2 0 2
        +-- 127.0.0.0/31 1 0 0
           |-- 127.0.0.0
              /32 link BROADCAST
              /8 host LOCAL
           |-- 127.0.0.1
              /32 host LOCAL
Local:
  +-- 0.0.0.0/0 3 0 5
     |-- 10.0.0.5
        /32 host LOCAL
";

#[test]
fn host_routes_of_type_local_are_this_hosts_addresses_each_once() {
    assert_eq!(
        parse_fib_trie_local(TABLE),
        vec![Ipv4Addr::new(10, 0, 0, 5), Ipv4Addr::new(127, 0, 0, 1)]
    );
}

#[test]
fn broadcast_leaves_and_whole_networks_are_not_addresses() {
    let found = parse_fib_trie_local(TABLE);

    assert!(!found.contains(&Ipv4Addr::new(10, 0, 0, 255)));
    assert!(!found.contains(&Ipv4Addr::new(10, 0, 0, 0)));
    assert!(
        !found.contains(&Ipv4Addr::new(127, 0, 0, 0)),
        "/8 host LOCAL is a network"
    );
}

#[test]
fn text_that_is_not_a_table_lists_nothing() {
    assert!(parse_fib_trie_local("").is_empty());
    assert!(
        parse_fib_trie_local("garbage\n/32 host LOCAL\n|-- not-an-address\n/32 host LOCAL\n")
            .is_empty()
    );
}

#[test]
fn a_local_route_before_any_leaf_is_ignored() {
    assert!(parse_fib_trie_local("/32 host LOCAL\n").is_empty());
}

/// The review gap: a `local` route added by hand for one address prints `/32 universe
/// LOCAL`, and the host answers on it exactly as on an interface address.
#[test]
fn a_single_address_local_route_of_any_scope_is_an_address() {
    let table = "Local:
  +-- 0.0.0.0/0 3 0 5
     |-- 192.0.2.5
        /32 universe LOCAL
     |-- 192.0.2.6
        /32 link LOCAL
";

    assert_eq!(
        parse_fib_trie_local(table),
        vec![Ipv4Addr::new(192, 0, 2, 5), Ipv4Addr::new(192, 0, 2, 6)]
    );
}

/// Inverse control: the scope never makes a non-local route an address.
#[test]
fn a_single_address_route_that_is_not_local_is_not_an_address_whatever_its_scope() {
    let table = "Main:
     |-- 192.0.2.5
        /32 universe UNICAST
     |-- 192.0.2.6
        /32 host BROADCAST
";

    assert!(parse_fib_trie_local(table).is_empty());
}
