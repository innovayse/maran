//! The pre-swap layer: run the daemon against the text and see whether it lives.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::validate_candidate_config::validate_candidate_config;

#[test]
fn a_candidate_the_daemon_keeps_serving_is_accepted() {
    // The inverse control rules/testing.md requires: a validator mutated to
    // refuse everything passes every test that only ever feeds it broken input.
    let host = FakeFtpsHost::with_certificate();
    validate_candidate_config(&host, distro(), "listen=YES\n").expect("accepted");
}

#[test]
fn the_candidate_is_run_in_standalone_foreground_mode_on_a_loopback_port() {
    // Every one of these is load-bearing and measured: without -obackground=NO a
    // good config exits 0 on the RHEL family by forking; without -olisten=YES it
    // exits 2 on both families in inetd mode; and without the loopback address
    // and an unused port the check would collide with the daemon already serving
    // port 21.
    let host = FakeFtpsHost::with_certificate();
    validate_candidate_config(&host, distro(), "listen=YES\n").expect("accepted");

    let spawn = host.spawns().first().cloned().expect("the daemon was run");
    assert_eq!(spawn.argv[0], distro().vsftpd_binary().as_str());
    assert!(spawn.argv.contains(&"-obackground=NO".to_owned()));
    assert!(spawn.argv.contains(&"-olisten=YES".to_owned()));
    assert!(spawn.argv.contains(&"-olisten_ipv6=NO".to_owned()));
    assert!(
        spawn
            .argv
            .contains(&"-olisten_address=127.0.0.1".to_owned())
    );
    assert!(spawn.argv.contains(&"-olisten_port=50000".to_owned()));
}

#[test]
fn a_candidate_the_daemon_exits_on_is_refused_and_carries_what_it_printed() {
    let host = FakeFtpsHost::with_certificate().refusing_candidates();
    let refused = validate_candidate_config(&host, distro(), "not a config\n")
        .expect_err("the daemon exited");

    assert_eq!(
        refused,
        FtpsError::ConfigRejected {
            output: "500 OOPS: bad bool value in config file".to_owned(),
            output_is_unavailable_on_this_platform: false,
        }
    );
}

#[test]
fn a_refusal_on_a_platform_that_prints_nothing_says_so_rather_than_inventing_a_reason() {
    // The Debian family's build exits 2 with completely empty output for every
    // refusal it has. A message that reported "vsftpd said: " and stopped would
    // read as a daemon with nothing to say rather than as a build that never
    // speaks.
    let host = FakeFtpsHost::with_certificate().refusing_candidates_silently();
    let refused = validate_candidate_config(&host, distro(), "not a config\n")
        .expect_err("the daemon exited");

    assert_eq!(
        refused,
        FtpsError::ConfigRejected {
            output: String::new(),
            output_is_unavailable_on_this_platform: true,
        }
    );
}

#[test]
fn a_host_that_cannot_give_a_loopback_port_reports_a_failure_to_run_rather_than_a_refusal() {
    let host = FakeFtpsHost::with_certificate().without_ephemeral_ports();
    let failed =
        validate_candidate_config(&host, distro(), "listen=YES\n").expect_err("no port to bind");

    assert_eq!(failed, FtpsError::program_unavailable());
    assert!(
        host.spawns().is_empty(),
        "no daemon may be started without a port of its own"
    );
}

#[test]
fn the_candidate_has_no_call_class_because_the_end_of_its_window_is_a_success() {
    // A candidate that outlives its window is the ACCEPTED outcome, so it is not
    // run under a class whose deadline would kill it as wedged.
    let host = FakeFtpsHost::with_certificate();
    validate_candidate_config(&host, distro(), "listen=YES\n").expect("accepted");

    let spawn = host.spawns().first().cloned().expect("the daemon was run");
    assert_eq!(spawn.class, None);
}

#[test]
fn the_candidate_is_given_no_end_of_options_marker() {
    // The daemon's own argument loop treats `--` as an unknown option and exits
    // 2, which would refuse every valid configuration.
    let host = FakeFtpsHost::with_certificate();
    validate_candidate_config(&host, distro(), "listen=YES\n").expect("accepted");

    let spawn = host.spawns().first().cloned().expect("the daemon was run");
    assert!(
        !spawn.argv.iter().any(|argument| argument == "--"),
        "{:?}",
        spawn.argv
    );
}

#[test]
fn a_daemon_that_floods_its_output_is_a_bound_and_never_a_verdict_on_the_text() {
    // A daemon killed for printing too much has said nothing about the
    // configuration. Reading it as a refusal would blame the text; reading it
    // as an acceptance would swap in a configuration nobody saw run.
    let host = FakeFtpsHost::with_certificate().with_candidate_flooding();

    let failed =
        validate_candidate_config(&host, distro(), "listen=YES\n").expect_err("the daemon flooded");

    assert!(matches!(failed, FtpsError::Bound(_)), "got {failed:?}");
}
