//! Tests for `PrimaryInterface::from_ipv4_routes`: picking the interface
//! carrying the lowest-metric IPv4 default route out of `/proc/net/route`'s
//! text.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::PrimaryInterface;

/// One interface, one default route — the ordinary case.
const SINGLE_DEFAULT: &str = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n\
eth0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n";

#[test]
fn the_interface_of_the_only_default_route_is_reported() {
    let interface = PrimaryInterface::from_ipv4_routes(SINGLE_DEFAULT);

    assert_eq!(interface, PrimaryInterface::Present("eth0".to_owned()));
}

/// A route to a specific network (a non-zero `Destination`) is not a default
/// route and must not be mistaken for one.
#[test]
fn a_non_default_route_is_ignored() {
    let routes = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n\
eth0\t000011AC\t00000000\t0001\t0\t0\t0\t0000FFFF\t0\t0\t0\n";

    let interface = PrimaryInterface::from_ipv4_routes(routes);

    assert_eq!(interface, PrimaryInterface::NotAvailable);
}

/// No routes at all — just the header — reports `NotAvailable`, not a panic
/// or a fabricated interface name.
#[test]
fn an_empty_table_is_not_available() {
    let routes =
        "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n";

    let interface = PrimaryInterface::from_ipv4_routes(routes);

    assert_eq!(interface, PrimaryInterface::NotAvailable);
}

/// Two default routes, two different metrics: the LOWER metric wins, exactly
/// the tie-break the kernel itself applies — this is the definition's whole
/// point, so it is the case most worth pinning.
#[test]
fn the_lower_metric_default_route_wins() {
    let routes = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n\
wlan0\t00000000\t0101A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n\
eth0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n";

    let interface = PrimaryInterface::from_ipv4_routes(routes);

    assert_eq!(interface, PrimaryInterface::Present("eth0".to_owned()));
}

/// A line with fewer columns than a real `/proc/net/route` row is skipped
/// rather than panicking the root agent on a kernel format this crate did not
/// anticipate.
#[test]
fn a_malformed_line_is_skipped_without_panicking() {
    let routes = "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n\
short\tline\n\
eth0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n";

    let interface = PrimaryInterface::from_ipv4_routes(routes);

    assert_eq!(interface, PrimaryInterface::Present("eth0".to_owned()));
}
