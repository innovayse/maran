//! Asking the daemon's own checker whether this build can sign.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::dns::fake_dns_host::{FakeDnsHost, distro};
use crate::dns::probe_dnssec_support::probe_dnssec_support;

#[test]
fn a_checker_that_accepts_the_probe_means_the_build_can_sign() {
    let host = FakeDnsHost::new();

    assert!(probe_dnssec_support(&host, distro()));
}

#[test]
fn a_checker_that_refuses_the_probe_means_the_build_cannot_sign() {
    // The planted failure, with the accepting control above.
    let host = FakeDnsHost::new().with_dnssec_unsupported();

    assert!(!probe_dnssec_support(&host, distro()));
}

#[test]
fn the_checker_is_run_on_the_probe_file_and_the_probe_states_the_policy() {
    let host = FakeDnsHost::new();
    let _ = probe_dnssec_support(&host, distro());

    let spawns = host.spawns();
    assert_eq!(spawns.len(), 1);
    assert_eq!(
        spawns[0],
        [
            "/usr/bin/named-checkconf",
            "/etc/maran/bind/dnssec-probe.conf"
        ]
    );
    let probes = host.probes();
    assert_eq!(probes.len(), 1);
    assert!(probes[0].contains("dnssec-policy \"maran-csk\""));
}

#[test]
fn the_probe_is_not_the_real_configuration_path() {
    // The probe must never overwrite the file the daemon is started against.
    let host = FakeDnsHost::new();
    let _ = probe_dnssec_support(&host, distro());

    assert!(host.written_configs().is_empty());
    assert!(host.live_config().is_none());
}
