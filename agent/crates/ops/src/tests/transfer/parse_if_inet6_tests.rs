//! Tests for [`parse_if_inet6`].

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::Ipv6Addr;

use super::parse_if_inet6;

/// Rows in the kernel's format: the loopback and a global address.
const TABLE: &str = "00000000000000000000000000000001 01 80 10 80       lo
20010db8000000000000000000000005 02 40 00 80     eth0
";

#[test]
fn each_row_is_an_address() {
    assert_eq!(
        parse_if_inet6(TABLE),
        vec![
            Ipv6Addr::LOCALHOST,
            "2001:db8::5".parse::<Ipv6Addr>().unwrap()
        ]
    );
}

#[test]
fn a_repeated_row_is_listed_once() {
    let twice = format!("{TABLE}{TABLE}");

    assert_eq!(parse_if_inet6(&twice).len(), 2);
}

#[test]
fn rows_that_are_not_32_hex_digits_are_skipped() {
    let text = "short 01 80 10 80 lo
0000000000000000000000000000000g 01 80 10 80 lo
000000000000000000000000000000010 01 80 10 80 lo

";

    assert!(parse_if_inet6(text).is_empty());
}
