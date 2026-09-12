//! Which listening socket the kernel will give the daemon, classified by errno.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::ErrorKind;

use crate::ftps::fake_ftps_host::FakeFtpsHost;
use crate::ftps::model::listen_mode::ListenMode;
use crate::ftps::probe_listen_mode::probe_listen_mode;

#[test]
fn a_kernel_that_binds_the_dual_stack_socket_selects_the_dual_stack_mode() {
    let host = FakeFtpsHost::with_certificate();
    assert_eq!(probe_listen_mode(&host), ListenMode::DualStack);
}

#[test]
fn a_kernel_that_answers_eafnosupport_selects_the_ipv4_only_mode() {
    let host = FakeFtpsHost::with_certificate().refusing_ipv6_bind(ErrorKind::Unsupported);
    assert_eq!(probe_listen_mode(&host), ListenMode::Ipv4Only);
}

#[test]
fn a_kernel_that_answers_eaddrnotavail_selects_the_ipv4_only_mode() {
    let host = FakeFtpsHost::with_certificate().refusing_ipv6_bind(ErrorKind::AddrNotAvailable);
    assert_eq!(probe_listen_mode(&host), ListenMode::Ipv4Only);
}

#[test]
fn a_refusal_that_is_not_evidence_about_address_families_keeps_the_dual_stack_default() {
    // The asymmetry is the whole reason this is written down: a wrong dual-stack
    // answer is caught by the daemon's own start and rolled back, while a wrong
    // IPv4-only answer produces a daemon that comes up perfectly and serves no
    // IPv6 client at all, which nothing downstream can observe.
    for kind in [
        ErrorKind::PermissionDenied,
        ErrorKind::AddrInUse,
        ErrorKind::Other,
    ] {
        let host = FakeFtpsHost::with_certificate().refusing_ipv6_bind(kind);
        assert_eq!(
            probe_listen_mode(&host),
            ListenMode::DualStack,
            "{kind:?} is not evidence about address families"
        );
    }
}
