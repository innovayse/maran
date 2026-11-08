//! Turning DNS off, and keeping it off across a reboot.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::dns::disable_dns::disable_dns;
use crate::dns::dns_error::DnsError;
use crate::dns::fake_dns_host::{FakeDnsHost, distro};

#[test]
fn disabling_stops_the_daemon_and_takes_the_unit_out_of_the_boot_sequence() {
    let host = FakeDnsHost::new().with_running_daemon();
    let state = disable_dns(&host, distro()).unwrap();

    assert!(!state.running);
    assert!(!state.enabled_at_boot);
    assert!(!host.is_enabled());

    let disable = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.get(1).is_some_and(|verb| verb == "disable"))
        .expect("the unit was disabled");
    assert_eq!(
        disable[1..],
        ["disable", "--now", "--", "maran-dns.service"]
    );
}

#[test]
fn disabling_a_daemon_that_is_already_off_converges_and_is_not_an_error() {
    let host = FakeDnsHost::new();
    let state = disable_dns(&host, distro()).unwrap();

    assert!(!state.running);
}

#[test]
fn disabling_leaves_the_configuration_exactly_where_it_is() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n};\n");
    disable_dns(&host, distro()).unwrap();

    assert_eq!(host.live_config().as_deref(), Some("options {\n};\n"));
    assert!(host.written_configs().is_empty());
}

#[test]
fn a_refusal_by_the_service_manager_is_an_error_and_never_a_reported_stop() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_failing_disable();

    assert_eq!(
        disable_dns(&host, distro()).unwrap_err(),
        DnsError::ServiceRefused {
            unit: "maran-dns.service".to_owned()
        }
    );
    assert!(host.is_running());
}
