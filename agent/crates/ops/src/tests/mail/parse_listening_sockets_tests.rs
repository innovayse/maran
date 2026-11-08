//! Reading the listening sockets out of the kernel's table text.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::model::listening_socket::ListeningSocket;
use crate::mail::parse_listening_sockets::parse_listening_sockets;

const V4_HEADER: &str = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode";

/// A row as the kernel prints it for an IPv4 socket.
fn v4_row(address: &str, port: &str, state: &str) -> String {
    format!(
        "   0: {address}:{port} 00000000:0000 {state} 00000000:00000000 00:00000000 00000000     0        0 12345 1 0000000000000000 100 0 0 10 0"
    )
}

#[test]
fn a_public_ipv4_listener_is_reported_with_its_port() {
    let table = format!("{V4_HEADER}\n{}\n", v4_row("00000000", "0019", "0A"));

    assert_eq!(
        parse_listening_sockets(&table, ""),
        vec![ListeningSocket {
            port: 25,
            loopback_only: false
        }]
    );
}

#[test]
fn a_loopback_ipv4_listener_is_marked_loopback_only() {
    let table = format!("{}\n", v4_row("0100007F", "01BB", "0A"));

    assert_eq!(
        parse_listening_sockets(&table, ""),
        vec![ListeningSocket {
            port: 443,
            loopback_only: true
        }]
    );
}

#[test]
fn a_socket_that_is_not_listening_is_not_reported() {
    // The inverse control: the same address and port in an established state.
    let table = format!("{}\n", v4_row("00000000", "0019", "01"));

    assert!(parse_listening_sockets(&table, "").is_empty());
}

#[test]
fn the_header_line_is_not_read_as_a_socket() {
    assert!(parse_listening_sockets(V4_HEADER, "").is_empty());
}

#[test]
fn an_ipv6_wildcard_listener_is_public_and_the_ipv6_loopback_is_not() {
    let wildcard = "   0: 00000000000000000000000000000000:01E0 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 1 1 0 100 0 0 10 0";
    let loopback = "   1: 00000000000000000000000001000000:0050 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 2 1 0 100 0 0 10 0";
    let mapped_loopback = "   2: 0000000000000000FFFF00000100007F:0051 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 3 1 0 100 0 0 10 0";
    let table = format!("{wildcard}\n{loopback}\n{mapped_loopback}\n");

    assert_eq!(
        parse_listening_sockets("", &table),
        vec![
            ListeningSocket {
                port: 480,
                loopback_only: false
            },
            ListeningSocket {
                port: 80,
                loopback_only: true
            },
            ListeningSocket {
                port: 81,
                loopback_only: true
            },
        ]
    );
}

#[test]
fn a_row_of_the_wrong_shape_is_skipped_and_never_guessed_at() {
    let table =
        "garbage\n   0: nothex:0019 00000000:0000 0A\n   1: 0000000000:0019 00000000:0000 0A\n";

    assert!(parse_listening_sockets(table, "").is_empty());
}

#[test]
fn both_tables_are_read() {
    let v4 = format!("{}\n", v4_row("00000000", "0019", "0A"));
    let v6 = "   0: 00000000000000000000000000000000:01D1 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 1 1 0 100 0 0 10 0\n";

    assert_eq!(parse_listening_sockets(&v4, v6).len(), 2);
}
