//! Making a running daemon pick up certificate material replaced underneath it.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::reload_ftps_tls::reload_ftps_tls;

#[test]
fn reloading_tls_restarts_a_running_daemon() {
    // vsftpd has no signal that re-reads its TLS material, so a restart is the
    // operation and not a shortcut.
    let host = FakeFtpsHost::with_certificate().with_running_daemon();
    let state = reload_ftps_tls(&host, distro()).expect("reloaded");

    assert_eq!(host.restart_count(), 1);
    assert!(state.running);
    assert!(state.control_port_answered);
}

#[test]
fn reloading_tls_on_a_stopped_daemon_touches_nothing_and_succeeds() {
    // A daemon that is off reads the new material at its next start by
    // construction, and a TLS reload must never be the thing that starts a
    // deliberately-disabled daemon.
    let host = FakeFtpsHost::with_certificate();
    let state = reload_ftps_tls(&host, distro()).expect("nothing to do");

    assert_eq!(host.restart_count(), 0);
    assert!(!state.running);
}

#[test]
fn a_daemon_that_stops_answering_after_the_restart_is_reported_as_not_listening() {
    // The measured shape of a certificate whose key does not match: it parses
    // perfectly and dies at the first handshake, leaving a unit systemd is happy
    // with.
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .silent_on_control_port();

    assert_eq!(
        reload_ftps_tls(&host, distro()).expect_err("silent port"),
        FtpsError::NotListening
    );
}

#[test]
fn a_daemon_the_service_manager_will_not_bring_back_is_reported_as_a_refusal() {
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .refusing_restart();
    let refused = reload_ftps_tls(&host, distro()).expect_err("would not restart");

    assert!(matches!(refused, FtpsError::ServiceRefused { .. }));
}

#[test]
fn the_configuration_file_is_never_rewritten_by_a_tls_reload() {
    // The material changed, not the configuration; a reload that rewrote the file
    // would be an enable wearing another name.
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .with_live_config("listen=NO\nlisten_ipv6=YES\n");
    reload_ftps_tls(&host, distro()).expect("reloaded");

    assert!(host.written_configs().is_empty());
    assert_eq!(
        host.live_config().as_deref(),
        Some("listen=NO\nlisten_ipv6=YES\n")
    );
}

#[test]
fn a_service_manager_that_cannot_be_started_is_an_error_and_never_a_quiet_no_op() {
    let host = FakeFtpsHost::with_certificate().without_service_manager();
    assert_eq!(
        reload_ftps_tls(&host, distro()).expect_err("no service manager"),
        FtpsError::program_unavailable()
    );
}
