//! A status that observes the host, and reads the file the way the daemon does.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::domain::Domain;

use crate::ftps::fake_ftps_host::{FakeFtpsHost, HOSTNAME, distro};
use crate::ftps::ftps_error::FtpsError;
use crate::ftps::get_ftps_status::get_ftps_status;
use crate::ftps::model::listen_mode::ListenMode;

/// The hostname the status asks about when it asks about one.
fn hostname() -> Domain {
    Domain::parse(HOSTNAME).unwrap()
}

/// A live configuration in the shape the template renders: dual stack, forced
/// TLS on both channels, a passive range.
fn dual_stack_config() -> String {
    [
        "# Rendered by the Maran agent.",
        "listen=NO",
        "listen_ipv6=YES",
        "listen_port=21",
        "pasv_min_port=49152",
        "pasv_max_port=50192",
        "force_local_logins_ssl=YES",
        "force_local_data_ssl=YES",
        "",
    ]
    .join("\n")
}

#[test]
fn the_listening_mode_is_read_from_the_file_the_daemon_serves() {
    let host = FakeFtpsHost::with_certificate().with_live_config(&dual_stack_config());
    let state = get_ftps_status(&host, distro(), None).expect("read");

    assert_eq!(state.listen_mode, Some(ListenMode::DualStack));
    assert_eq!(state.passive_port_min, Some(49_152));
    assert_eq!(state.passive_port_max, Some(50_192));
    assert_eq!(state.forced_tls, Some(true));
}

#[test]
fn the_ipv4_only_pair_in_the_live_file_is_reported_as_the_ipv4_only_mode() {
    let config =
        dual_stack_config().replace("listen=NO\nlisten_ipv6=YES", "listen=YES\nlisten_ipv6=NO");
    let host = FakeFtpsHost::with_certificate().with_live_config(&config);

    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .listen_mode,
        Some(ListenMode::Ipv4Only)
    );
}

#[test]
fn a_planted_duplicate_listen_pair_is_reported_as_the_last_occurrence_and_not_the_first() {
    // The case that motivated the rule. vsftpd takes the LAST occurrence of a
    // key, so a file the agent rendered dual-stack and something appended an
    // IPv4-only pair to is a daemon listening on IPv4 only. A read that answered
    // with the first would report the mode the PANEL intended while the daemon
    // obeyed the other one — a check that agrees with the panel whatever the host
    // is doing.
    let planted = format!("{}listen=YES\nlisten_ipv6=NO\n", dual_stack_config());
    let host = FakeFtpsHost::with_certificate().with_live_config(&planted);

    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .listen_mode,
        Some(ListenMode::Ipv4Only),
        "the appended pair is the one the daemon obeys"
    );
}

#[test]
fn a_planted_force_local_logins_ssl_no_is_reported_as_forced_tls_switched_off() {
    // The single appended line that unmakes this whole feature's promise, on a
    // file that still parses and a daemon that still starts. Neither validation
    // layer of an enable can see it; this read is what can.
    let planted = format!("{}force_local_logins_ssl=NO\n", dual_stack_config());
    let host = FakeFtpsHost::with_certificate().with_live_config(&planted);

    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .forced_tls,
        Some(false)
    );
}

#[test]
fn a_planted_force_local_data_ssl_no_is_reported_as_forced_tls_switched_off() {
    let planted = format!("{}force_local_data_ssl=NO\n", dual_stack_config());
    let host = FakeFtpsHost::with_certificate().with_live_config(&planted);

    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .forced_tls,
        Some(false)
    );
}

#[test]
fn a_planted_duplicate_passive_range_is_reported_as_the_last_occurrence() {
    let planted = format!(
        "{}pasv_min_port=30000\npasv_max_port=30100\n",
        dual_stack_config()
    );
    let host = FakeFtpsHost::with_certificate().with_live_config(&planted);
    let state = get_ftps_status(&host, distro(), None).expect("read");

    assert_eq!(state.passive_port_min, Some(30_000));
    assert_eq!(state.passive_port_max, Some(30_100));
}

#[test]
fn a_commented_out_key_is_not_a_setting() {
    let planted = format!("{}# force_local_logins_ssl=NO\n", dual_stack_config());
    let host = FakeFtpsHost::with_certificate().with_live_config(&planted);

    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .forced_tls,
        Some(true)
    );
}

#[test]
fn a_host_with_no_live_configuration_says_it_does_not_know_rather_than_answering_a_default() {
    let host = FakeFtpsHost::with_certificate();
    let state = get_ftps_status(&host, distro(), None).expect("read");

    assert_eq!(state.listen_mode, None);
    assert_eq!(state.forced_tls, None);
    assert_eq!(state.passive_port_min, None);
    assert_eq!(state.passive_port_max, None);
}

#[test]
fn running_is_the_service_managers_answer_and_never_this_functions_own() {
    let stopped = FakeFtpsHost::with_certificate();
    assert!(
        !get_ftps_status(&stopped, distro(), None)
            .expect("read")
            .running
    );

    let up = FakeFtpsHost::with_certificate().with_running_daemon();
    assert!(get_ftps_status(&up, distro(), None).expect("read").running);
}

#[test]
fn a_running_daemon_that_is_silent_on_the_control_port_is_reported_as_not_answering() {
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .silent_on_control_port();
    let state = get_ftps_status(&host, distro(), None).expect("read");

    assert!(state.running, "the unit is active");
    assert!(
        !state.control_port_answered,
        "and nothing is serving on the control port"
    );
}

#[test]
fn a_greeting_that_is_not_a_220_is_not_an_answer() {
    // 421 is what a daemon that has decided to refuse the session says. A check
    // that only asked "did anything arrive" would call that healthy.
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .greeting_of("421 Service not available\r\n");

    assert!(
        !get_ftps_status(&host, distro(), None)
            .expect("read")
            .control_port_answered
    );
}

#[test]
fn no_hostname_asks_no_question_about_certificate_material() {
    let host = FakeFtpsHost::with_certificate();
    assert_eq!(
        get_ftps_status(&host, distro(), None)
            .expect("read")
            .certificate,
        None
    );
}

#[test]
fn a_hostname_is_answered_with_the_material_the_store_holds_for_it() {
    let host = FakeFtpsHost::with_certificate();
    let certificate = get_ftps_status(&host, distro(), Some(&hostname()))
        .expect("read")
        .certificate
        .expect("a question was asked");

    assert!(certificate.present);
    assert_eq!(
        certificate.certificate_path,
        "/etc/maran/certificates/ftp.example.test/fullchain.pem"
    );
}

#[test]
fn a_configuration_that_exists_and_cannot_be_read_is_an_error_and_never_an_empty_status() {
    let host = FakeFtpsHost::with_certificate().unreadable_config();
    assert_eq!(
        get_ftps_status(&host, distro(), None).expect_err("unreadable"),
        FtpsError::ConfigUnreadable
    );
}

#[test]
fn material_that_cannot_be_read_is_an_error_and_never_a_confident_absent() {
    let host = FakeFtpsHost::with_certificate().unreadable_certificate();
    assert_eq!(
        get_ftps_status(&host, distro(), Some(&hostname())).expect_err("unreadable"),
        FtpsError::ConfigUnreadable
    );
}

#[test]
fn a_host_whose_service_manager_cannot_be_started_is_an_error_and_never_a_stopped_daemon() {
    let host = FakeFtpsHost::with_certificate().without_service_manager();
    assert_eq!(
        get_ftps_status(&host, distro(), None).expect_err("no service manager"),
        FtpsError::program_unavailable()
    );
}
