//! Tests for [`parse_fib_trie`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::Ipv4Addr;

use super::parse_fib_trie;

#[test]
fn every_route_is_listed_under_its_leaf_with_its_scope_and_type() {
    let routes = parse_fib_trie("   |-- 10.0.0.5\n      /32 host LOCAL\n      /24 link UNICAST\n");

    assert_eq!(routes.len(), 2);
    assert_eq!(routes[0].leaf, "10.0.0.5".parse::<Ipv4Addr>().unwrap());
    assert_eq!(routes[0].length, 32);
    assert_eq!(routes[0].scope, "host");
    assert_eq!(routes[0].kind, "LOCAL");
    assert_eq!(routes[1].kind, "UNICAST");
}

#[test]
fn lines_that_are_not_routes_are_skipped() {
    // The inverse control: headers, a route before any leaf, a leaf that is not an
    // address and a route with a missing or non-numeric length list nothing.
    let text = "Main:\n/32 host LOCAL\n|-- not-an-address\n/32 host LOCAL\n|-- 10.0.0.5\n/xx host LOCAL\n/32 host\n/32 host LOCAL extra\n";

    assert!(parse_fib_trie(text).is_empty());
}

#[test]
fn an_empty_table_lists_nothing() {
    assert!(parse_fib_trie("").is_empty());
}
