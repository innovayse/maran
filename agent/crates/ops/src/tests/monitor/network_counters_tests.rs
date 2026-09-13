//! Tests for `NetworkCounters`: summing the kernel's per-interface byte
//! counters.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::NetworkCounters;
use crate::monitor::fake_monitor_host::{ALMA_NET_DEV, UBUNTU_NET_DEV};

/// The two header lines both captures begin with, verbatim.
const HEADERS: &str = "Inter-|   Receive                                                |  Transmit\n \
                       face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n";

#[test]
fn the_captured_ubuntu_net_dev_is_read() {
    let counters = NetworkCounters::parse(UBUNTU_NET_DEV).expect("the capture names an interface");

    assert_eq!(counters.received_bytes, 90);
    assert_eq!(counters.transmitted_bytes, 42);
}

#[test]
fn the_captured_alma_net_dev_is_read() {
    let counters = NetworkCounters::parse(ALMA_NET_DEV).expect("the capture names an interface");

    assert_eq!(counters.received_bytes, 90);
    assert_eq!(counters.transmitted_bytes, 42);
}

#[test]
fn loopback_traffic_is_not_network_traffic() {
    // Every request nginx forwards to php-fpm, every query the panel sends to
    // its own database and every byte of a local backup crosses `lo`. Counting
    // it reports a host with no visitors as busy, and double-counts every real
    // request, which arrives on a real interface and is then proxied over the
    // loopback.
    //
    // The counters here are written rather than captured on purpose: both
    // polygon images are freshly started and report `lo: 0`, so a test built on
    // the captures alone could not tell this filter from its absence.
    let net_dev = format!(
        "{HEADERS}\
             lo: 5000000  1000    0    0    0     0          0         0  5000000    1000    0    0    0     0       0          0\n\
          eth0:     900     10    0    0    0     0          0         0      420       5    0    0    0     0       0          0\n"
    );

    let counters = NetworkCounters::parse(&net_dev).expect("eth0 is an interface");

    assert_eq!(counters.received_bytes, 900);
    assert_eq!(counters.transmitted_bytes, 420);
}

#[test]
fn several_interfaces_are_summed() {
    let net_dev = format!(
        "{HEADERS}\
          eth0:     900     10    0    0    0     0          0         0      420       5    0    0    0     0       0          0\n\
          eth1:     100      1    0    0    0     0          0         0       80       1    0    0    0     0       0          0\n"
    );

    let counters = NetworkCounters::parse(&net_dev).expect("both interfaces are read");

    assert_eq!(counters.received_bytes, 1000);
    assert_eq!(counters.transmitted_bytes, 500);
}

#[test]
fn the_header_lines_are_not_an_interface() {
    // A host with only headers names no interface at all, so there is nothing
    // to report rather than a host that carried zero bytes.
    assert!(NetworkCounters::parse(HEADERS).is_none());
}

#[test]
fn a_host_with_only_a_loopback_carried_nothing() {
    let net_dev = format!(
        "{HEADERS}\
             lo:  500   5    0    0    0     0          0         0      500       5    0    0    0     0       0          0\n"
    );

    let counters = NetworkCounters::parse(&net_dev).expect("the loopback is still an interface");

    assert_eq!(counters.received_bytes, 0);
    assert_eq!(counters.transmitted_bytes, 0);
}

#[test]
fn an_interface_whose_counter_touches_the_colon_is_still_read() {
    // The kernel's column is fixed width, so a long-lived interface's byte
    // count grows into the colon and the usual space disappears.
    let net_dev = format!("{HEADERS}  eth0:12345678901 10 0 0 0 0 0 0 420 5 0 0 0 0 0 0\n");

    let counters = NetworkCounters::parse(&net_dev).expect("the line is still an interface");

    assert_eq!(counters.received_bytes, 12_345_678_901);
    assert_eq!(counters.transmitted_bytes, 420);
}

#[test]
fn a_line_with_too_few_counters_is_skipped_and_the_rest_are_summed() {
    let net_dev = format!(
        "{HEADERS}\
          eth0: 1 2 3\n\
          eth1:     100      1    0    0    0     0          0         0       80       1    0    0    0     0       0          0\n"
    );

    let counters = NetworkCounters::parse(&net_dev).expect("eth1 is readable");

    assert_eq!(counters.received_bytes, 100);
    assert_eq!(counters.transmitted_bytes, 80);
}

#[test]
fn a_token_that_is_not_a_number_skips_the_line_rather_than_shifting_the_fields_after_it() {
    // The protection this pins: the counters are taken and parsed as a BLOCK,
    // so an unreadable token refuses the whole line. Dropping it instead would
    // slide every later field one place left and read this line's packet count
    // (1) out of the byte-count slot — a plausible number, silently wrong,
    // reported to an operator as traffic. `eth1` is here so the test tells
    // "the bad line was skipped" from "nothing was read at all".
    let net_dev = format!(
        "{HEADERS}\
          eth0:     111   nope    0    0    0     0          0         0      222       1    0    0    0     0       0          0\n\
          eth1:     100      1    0    0    0     0          0         0       80       1    0    0    0     0       0          0\n"
    );

    let counters = NetworkCounters::parse(&net_dev).expect("eth1 is readable");

    assert_eq!(counters.received_bytes, 100);
    assert_eq!(counters.transmitted_bytes, 80);
    assert_ne!(
        counters.received_bytes, 211,
        "the unreadable token was dropped and every field after it shifted left"
    );
}

#[test]
fn text_with_no_interface_at_all_is_not_understood() {
    assert!(NetworkCounters::parse("").is_none());
}
