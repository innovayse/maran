//! The one reading of an observed daemon, and what each absent value means.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::ftps::{FtpsState, ListenMode};
use maran_ops::ssl::CertificateState;

use crate::services::ftps::ftps_status_fields::FtpsStatusFields;

/// A state with nothing observed: no daemon, no configuration, no hostname
/// asked about.
fn nothing_observed() -> FtpsState {
    FtpsState {
        running: false,
        control_port_answered: false,
        certificate: None,
        forced_tls: None,
        passive_port_min: None,
        passive_port_max: None,
        listen_mode: None,
    }
}

/// A state describing a healthy dual-stack daemon with real material.
fn a_running_daemon() -> FtpsState {
    FtpsState {
        running: true,
        control_port_answered: true,
        certificate: Some(CertificateState {
            certificate_path: "/etc/maran/ssl/ftp.example.test/fullchain.pem".to_owned(),
            private_key_path: "/etc/maran/ssl/ftp.example.test/privkey.pem".to_owned(),
            present: true,
            is_self_signed_placeholder: false,
        }),
        forced_tls: Some(true),
        passive_port_min: Some(30000),
        passive_port_max: Some(30099),
        listen_mode: Some(ListenMode::DualStack),
    }
}

#[test]
fn a_running_daemons_facts_reach_the_wire_unchanged() {
    let ok = FtpsStatusFields::from_state(a_running_daemon()).into_status_ok();

    assert!(ok.running);
    assert!(ok.control_port_answered);
    assert!(ok.certificate_present);
    assert!(!ok.certificate_is_self_signed);
    assert_eq!(
        ok.certificate_path,
        "/etc/maran/ssl/ftp.example.test/fullchain.pem"
    );
    assert_eq!(ok.passive_port_min, 30000);
    assert_eq!(ok.passive_port_max, 30099);
    assert!(!ok.ipv4_only);
    assert!(ok.forced_tls);
}

#[test]
fn the_ipv4_only_fallback_is_the_only_thing_that_sets_that_flag() {
    let mut state = a_running_daemon();
    state.listen_mode = Some(ListenMode::Ipv4Only);

    assert!(
        FtpsStatusFields::from_state(state)
            .into_status_ok()
            .ipv4_only
    );
}

#[test]
fn a_host_with_no_live_configuration_reports_the_dual_stack_default_and_no_range() {
    let ok = FtpsStatusFields::from_state(nothing_observed()).into_status_ok();

    assert!(
        !ok.ipv4_only,
        "an unread listen mode is the default the next enable will write, not the fallback"
    );
    assert_eq!(ok.passive_port_min, 0, "zero is not a port number");
    assert_eq!(ok.passive_port_max, 0);
}

#[test]
fn a_question_that_named_no_hostname_answers_with_no_certificate_facts_at_all() {
    let ok = FtpsStatusFields::from_state(nothing_observed()).into_status_ok();

    assert!(!ok.certificate_present);
    assert!(!ok.certificate_is_self_signed);
    assert_eq!(
        ok.certificate_path, "",
        "an empty path is what says nothing was asked; a caller that named no \
         hostname must not read these as 'there is no material'"
    );
}

#[test]
fn the_self_signed_placeholder_is_reported_as_present_and_as_a_placeholder() {
    let mut state = a_running_daemon();
    state.certificate = Some(CertificateState {
        certificate_path: "/etc/maran/ssl/ftp.example.test/fullchain.pem".to_owned(),
        private_key_path: "/etc/maran/ssl/ftp.example.test/privkey.pem".to_owned(),
        present: true,
        is_self_signed_placeholder: true,
    });

    let ok = FtpsStatusFields::from_state(state).into_status_ok();

    assert!(ok.certificate_present);
    assert!(ok.certificate_is_self_signed);
}

#[test]
fn a_configuration_this_agent_cannot_resolve_never_reports_encryption_as_enforced() {
    // The direction matters more than the value: an unknown that read as "TLS is
    // forced" would put a green screen in front of a daemon taking passwords in
    // the clear. False is what "this agent cannot say" answers with.
    let ok = FtpsStatusFields::from_state(nothing_observed()).into_status_ok();
    assert!(!ok.forced_tls);

    // And the positive control, so the assertion above is not satisfied by a
    // field that is always false.
    let mut state = a_running_daemon();
    state.forced_tls = Some(false);
    assert!(
        !FtpsStatusFields::from_state(state)
            .into_status_ok()
            .forced_tls
    );
    assert!(
        FtpsStatusFields::from_state(a_running_daemon())
            .into_status_ok()
            .forced_tls
    );
}

#[test]
fn the_four_responses_carry_the_same_nine_values_for_one_observation() {
    // The property the four messages exist for: a panel that has just enabled,
    // disabled or reloaded needs no second call. Four separate prost structs
    // means nothing couples them at compile time, so it is asserted here.
    let enable = FtpsStatusFields::from_state(a_running_daemon()).into_enable_ok();
    let disable = FtpsStatusFields::from_state(a_running_daemon()).into_disable_ok();
    let reload = FtpsStatusFields::from_state(a_running_daemon()).into_reload_ok();
    let status = FtpsStatusFields::from_state(a_running_daemon()).into_status_ok();

    for (name, running, answered, present, self_signed, path, min, max, ipv4, forced) in [
        (
            "enable",
            enable.running,
            enable.control_port_answered,
            enable.certificate_present,
            enable.certificate_is_self_signed,
            enable.certificate_path,
            enable.passive_port_min,
            enable.passive_port_max,
            enable.ipv4_only,
            enable.forced_tls,
        ),
        (
            "disable",
            disable.running,
            disable.control_port_answered,
            disable.certificate_present,
            disable.certificate_is_self_signed,
            disable.certificate_path,
            disable.passive_port_min,
            disable.passive_port_max,
            disable.ipv4_only,
            disable.forced_tls,
        ),
        (
            "reload",
            reload.running,
            reload.control_port_answered,
            reload.certificate_present,
            reload.certificate_is_self_signed,
            reload.certificate_path,
            reload.passive_port_min,
            reload.passive_port_max,
            reload.ipv4_only,
            reload.forced_tls,
        ),
    ] {
        assert_eq!(running, status.running, "{name}");
        assert_eq!(answered, status.control_port_answered, "{name}");
        assert_eq!(present, status.certificate_present, "{name}");
        assert_eq!(self_signed, status.certificate_is_self_signed, "{name}");
        assert_eq!(path, status.certificate_path, "{name}");
        assert_eq!(min, status.passive_port_min, "{name}");
        assert_eq!(max, status.passive_port_max, "{name}");
        assert_eq!(ipv4, status.ipv4_only, "{name}");
        assert_eq!(forced, status.forced_tls, "{name}");
    }
}
