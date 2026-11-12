//! Tests for [`parse_fib_trie_local_prefixes`].

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_fib_trie_local_prefixes;
use crate::utils::ipv4_prefix::Ipv4Prefix;

/// A trimmed table in the kernel's format: an interface address, a routed block
/// added with `ip route add local 192.0.2.0/24 dev lo`, and the loopback.
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
Local:
  +-- 0.0.0.0/0 3 0 5
     |-- 10.0.0.5
        /32 host LOCAL
     |-- 192.0.2.0
        /24 universe LOCAL
     +-- 127.0.0.0/8 2 0 2
        |-- 127.0.0.0
           /8 host LOCAL
        |-- 127.0.0.1
           /32 host LOCAL
";

fn prefix(text: &str, length: u8) -> Ipv4Prefix {
    Ipv4Prefix::new(text.parse().unwrap(), length).unwrap()
}

#[test]
fn a_local_route_wider_than_one_address_is_listed_with_the_loopback_range() {
    assert_eq!(
        parse_fib_trie_local_prefixes(TABLE),
        vec![prefix("192.0.2.0", 24), prefix("127.0.0.0", 8)]
    );
}

/// Inverse control: host routes, broadcast and unicast ranges are not local ranges.
#[test]
fn host_routes_and_non_local_ranges_are_not_listed() {
    let found = parse_fib_trie_local_prefixes(TABLE);

    assert!(!found.contains(&prefix("10.0.0.5", 32)));
    assert!(!found.contains(&prefix("10.0.0.0", 24)));
    assert!(!found.contains(&prefix("0.0.0.0", 0)));
}

#[test]
fn the_same_range_in_two_tables_is_listed_once() {
    let twice = format!("{TABLE}{TABLE}");

    assert_eq!(parse_fib_trie_local_prefixes(&twice).len(), 2);
}

#[test]
fn text_that_is_not_a_table_lists_nothing() {
    assert!(parse_fib_trie_local_prefixes("").is_empty());
    assert!(parse_fib_trie_local_prefixes("/24 universe LOCAL\n").is_empty());
    assert!(parse_fib_trie_local_prefixes("|-- not-an-address\n/24 universe LOCAL\n").is_empty());
    assert!(parse_fib_trie_local_prefixes("|-- 192.0.2.0\n/99 universe LOCAL\n").is_empty());
}
