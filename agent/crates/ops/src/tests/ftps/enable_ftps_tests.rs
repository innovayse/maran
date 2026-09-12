//! Turning FTPS on: what is refused, what is written, and what is put back.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::io::ErrorKind;

use crate::ftps::enable_ftps::enable_ftps;
use crate::ftps::fake_ftps_host::{FakeFtpsHost, configuration, configuration_behind_nat, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::model::listen_mode::ListenMode;

#[test]
fn enabling_ftps_for_a_hostname_with_no_certificate_material_refuses_and_names_the_path() {
    let host = FakeFtpsHost::new();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("no material");

    assert_eq!(
        refused,
        FtpsError::CertificateMissing {
            domain: "ftp.example.test".to_owned(),
            expected_path: "/etc/maran/certificates/ftp.example.test/fullchain.pem".to_owned(),
        }
    );
    assert!(
        host.written_configs().is_empty(),
        "nothing may be written before the material exists"
    );
    assert_eq!(host.restart_count(), 0);
}

#[test]
fn a_candidate_the_daemon_refuses_never_reaches_the_live_path() {
    let host = FakeFtpsHost::with_certificate().refusing_candidates();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("refused");

    assert!(matches!(refused, FtpsError::ConfigRejected { .. }));
    assert_eq!(
        host.live_config(),
        None,
        "the live config was written despite a refusal"
    );
    assert_eq!(host.restart_count(), 0);
}

#[test]
fn a_refusal_on_a_platform_that_prints_nothing_says_so_rather_than_inventing_a_reason() {
    let host = FakeFtpsHost::with_certificate().refusing_candidates_silently();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("refused");

    assert!(matches!(
        refused,
        FtpsError::ConfigRejected {
            ref output,
            output_is_unavailable_on_this_platform: true,
        } if output.is_empty()
    ));
}

#[test]
fn a_daemon_that_starts_but_does_not_answer_on_the_control_port_rolls_the_config_back() {
    // The failure a parse check cannot see: the unit is active and nothing is
    // listening. Measured shape — a certificate whose key does not match parses
    // fine and fails at the first handshake.
    let host = FakeFtpsHost::with_certificate()
        .with_live_config("previous")
        .silent_on_control_port();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("silent port");

    assert_eq!(refused, FtpsError::NotListening);
    assert_eq!(
        host.live_config().as_deref(),
        Some("previous"),
        "the previous configuration must be back byte for byte"
    );
    assert_eq!(
        host.restart_count(),
        2,
        "the rollback must restart the daemon on the restored config"
    );
}

#[test]
fn a_rollback_does_not_revert_a_configuration_another_operation_committed_after_it() {
    // F4. `previous` is captured before the config-write protocol takes its
    // lock, so a second enable committing in between makes it a superseded
    // value. Writing it back would roll back somebody else's SUCCESSFUL write
    // on the strength of a fact that had expired. There is no FTPS-area lock
    // and there is not going to be a sixth one, so the rollback re-reads and
    // refuses on a mismatch instead.
    let host = FakeFtpsHost::with_certificate()
        .with_live_config("previous")
        .with_a_neighbour_writing_after_the_swap("the neighbour's configuration")
        .silent_on_control_port();

    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("silent port");

    assert_eq!(refused, FtpsError::NotListening);
    assert_eq!(
        host.live_config().as_deref(),
        Some("the neighbour's configuration"),
        "the newer write must still be on disk: this operation's rollback has \
         nothing of its own left to take back"
    );
}

#[test]
fn a_unit_the_service_manager_will_not_start_rolls_the_config_back_and_returns_the_refusal() {
    let host = FakeFtpsHost::with_certificate()
        .with_live_config("previous")
        .refusing_restart();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("refused to start");

    assert!(matches!(refused, FtpsError::ServiceRefused { .. }));
    assert_eq!(host.live_config().as_deref(), Some("previous"));
}

#[test]
fn an_enable_with_nothing_to_go_back_to_stops_the_daemon_and_returns_the_original_error() {
    // There was no previous configuration, so there is nothing to restore. The
    // refused file stays on disk — it is inert, vsftpd reads it only at a start —
    // and what matters is that nothing is serving it and the caller is told why.
    let host = FakeFtpsHost::with_certificate().silent_on_control_port();
    let refused = enable_ftps(&host, distro(), &configuration()).expect_err("silent port");

    assert_eq!(refused, FtpsError::NotListening);
    assert!(
        host.spawns()
            .iter()
            .any(|spawn| spawn.argv.get(1).is_some_and(|verb| verb == "stop")),
        "the daemon must not be left serving a configuration that was rolled back"
    );
}

#[test]
fn applying_the_same_configuration_twice_writes_once_and_restarts_once() {
    // The inverse control: this feeds the gate a configuration it must ACCEPT, so
    // a validator mutated to refuse everything cannot pass the suite.
    let host = FakeFtpsHost::with_certificate();
    enable_ftps(&host, distro(), &configuration()).expect("first apply");
    enable_ftps(&host, distro(), &configuration()).expect("second apply");

    assert_eq!(host.written_configs().len(), 1);
    assert_eq!(
        host.restart_count(),
        1,
        "an idempotent apply must not bounce a running daemon"
    );
}

#[test]
fn a_changed_configuration_is_written_and_the_daemon_is_restarted() {
    // The other half of the idempotency claim: the comparison must be against the
    // bytes, not a flag saying "already done". A host holding a DIFFERENT config
    // is written to.
    let host = FakeFtpsHost::with_certificate();
    enable_ftps(&host, distro(), &configuration()).expect("first apply");
    enable_ftps(&host, distro(), &configuration_behind_nat("203.0.113.7")).expect("second apply");

    assert_eq!(host.written_configs().len(), 2);
    assert_eq!(host.restart_count(), 2);
    assert!(
        host.live_config()
            .expect("a config was written")
            .contains("pasv_address=203.0.113.7")
    );
}

#[test]
fn a_kernel_that_refuses_an_ipv6_bind_makes_the_enable_write_the_ipv4_only_config() {
    // The whole settlement, end to end: the probe's answer must reach the
    // rendered file AND the returned state, because the daemon binds from the
    // file and the screen states the mode from the state.
    let host = FakeFtpsHost::with_certificate().refusing_ipv6_bind(ErrorKind::Unsupported);
    let state = enable_ftps(&host, distro(), &configuration()).expect("enabled");

    assert_eq!(state.listen_mode, Some(ListenMode::Ipv4Only));
    let live = host.live_config().expect("a config was written");
    assert!(live.contains("listen=YES"));
    assert!(live.contains("listen_ipv6=NO"));
}

#[test]
fn a_host_with_ipv6_writes_the_dual_stack_config() {
    let host = FakeFtpsHost::with_certificate();
    let state = enable_ftps(&host, distro(), &configuration()).expect("enabled");

    assert_eq!(state.listen_mode, Some(ListenMode::DualStack));
    let live = host.live_config().expect("a config was written");
    assert!(live.contains("listen=NO"));
    assert!(live.contains("listen_ipv6=YES"));
}

#[test]
fn the_written_configuration_forces_tls_on_both_channels_and_the_state_says_so() {
    let host = FakeFtpsHost::with_certificate();
    let state = enable_ftps(&host, distro(), &configuration()).expect("enabled");

    assert_eq!(state.forced_tls, Some(true));
    let live = host.live_config().expect("a config was written");
    assert!(live.contains("force_local_logins_ssl=YES"));
    assert!(live.contains("force_local_data_ssl=YES"));
}

#[test]
fn the_candidate_is_validated_before_anything_is_swapped_in() {
    // Order, asserted as order: the daemon is run against the candidate before
    // the service manager is ever asked to restart anything.
    let host = FakeFtpsHost::with_certificate();
    enable_ftps(&host, distro(), &configuration()).expect("enabled");

    let spawns = host.spawns();
    let candidate = spawns
        .iter()
        .position(|spawn| spawn.argv[0] == distro().vsftpd_binary())
        .expect("the candidate was run");
    let restart = spawns
        .iter()
        .position(|spawn| spawn.argv.get(1).is_some_and(|verb| verb == "restart"))
        .expect("the daemon was restarted");

    assert!(
        candidate < restart,
        "the candidate check must precede the swap"
    );
}

#[test]
fn a_configuration_that_cannot_be_written_is_reported_as_a_write_failure_and_not_a_refusal() {
    let host = FakeFtpsHost::with_certificate().unwritable_config();
    let failed = enable_ftps(&host, distro(), &configuration()).expect_err("unwritable");

    assert!(matches!(failed, FtpsError::ConfigWrite { .. }));
}

#[test]
fn a_live_configuration_that_cannot_be_read_stops_the_enable_before_it_writes() {
    let host = FakeFtpsHost::with_certificate().unreadable_config();
    let failed = enable_ftps(&host, distro(), &configuration()).expect_err("unreadable");

    assert_eq!(failed, FtpsError::ConfigUnreadable);
    assert!(host.written_configs().is_empty());
}
