//! Turning FTPS off, and keeping it off across a reboot.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::ftps::disable_ftps::disable_ftps;
use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_error::FtpsError;

#[test]
fn disabling_stops_the_daemon_and_takes_the_unit_out_of_the_boot_sequence() {
    // Both halves in one command: stopping without disabling leaves the
    // operator's decision to be undone by the next power cut.
    let host = FakeFtpsHost::with_certificate().with_running_daemon();
    let state = disable_ftps(&host, distro()).expect("disabled");

    assert!(!state.running);
    assert!(!host.is_enabled());

    let disable = host
        .spawns()
        .into_iter()
        .find(|spawn| spawn.argv.get(1).is_some_and(|verb| verb == "disable"))
        .expect("the unit was disabled");
    assert_eq!(
        disable.argv[1..],
        ["disable", "--now", "maran-ftps.service"]
    );
}

#[test]
fn disabling_a_daemon_that_is_already_off_converges_and_is_not_an_error() {
    let host = FakeFtpsHost::with_certificate();
    let state = disable_ftps(&host, distro()).expect("already off");

    assert!(!state.running);
}

#[test]
fn disabling_leaves_the_configuration_file_exactly_where_it_is() {
    // Inert on disk, and keeping it means a later enable of the same
    // configuration converges without rewriting anything.
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .with_live_config("listen=NO\nlisten_ipv6=YES\n");
    disable_ftps(&host, distro()).expect("disabled");

    assert_eq!(
        host.live_config().as_deref(),
        Some("listen=NO\nlisten_ipv6=YES\n")
    );
    assert!(host.written_configs().is_empty());
}

#[test]
fn a_service_manager_that_cannot_be_started_is_an_error_and_never_a_reported_stop() {
    let host = FakeFtpsHost::with_certificate().without_service_manager();
    assert_eq!(
        disable_ftps(&host, distro()).expect_err("no service manager"),
        FtpsError::program_unavailable()
    );
}

#[test]
fn the_state_is_observed_after_the_command_rather_than_asserted_from_its_success() {
    // The command exiting zero is not evidence that the daemon stopped; the
    // service manager is asked again afterwards.
    let host = FakeFtpsHost::with_certificate().with_running_daemon();
    disable_ftps(&host, distro()).expect("disabled");

    let verbs: Vec<String> = host
        .spawns()
        .into_iter()
        .filter_map(|spawn| spawn.argv.get(1).cloned())
        .collect();
    assert_eq!(verbs, ["disable", "is-active"]);
}
