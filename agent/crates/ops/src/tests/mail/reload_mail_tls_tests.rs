//! Reloading the two daemons that hold the certificate.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::enable_mail::enable_mail;
use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro, settings};
use crate::mail::mail_error::MailError;
use crate::mail::reload_mail_tls::reload_mail_tls;

fn restarts_of(host: &FakeMailHost, unit: &str) -> usize {
    host.verbs_for(unit)
        .iter()
        .filter(|verb| *verb == "reload-or-restart")
        .count()
}

#[test]
fn only_postfix_and_dovecot_are_reloaded() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();
    let before: Vec<usize> = host
        .all_units()
        .iter()
        .map(|unit| restarts_of(&host, unit))
        .collect();

    reload_mail_tls(&host, distro(), VERSION_ID).unwrap();

    let after: Vec<usize> = host
        .all_units()
        .iter()
        .map(|unit| restarts_of(&host, unit))
        .collect();
    // Start order: key-value server, spam filter, Dovecot, Postfix.
    assert_eq!(after[0], before[0]);
    assert_eq!(after[1], before[1]);
    assert_eq!(after[2], before[2] + 1);
    assert_eq!(after[3], before[3] + 1);
}

#[test]
fn a_stopped_stack_is_left_stopped() {
    // Renewing a certificate must never be the thing that opens the mail ports.
    let host = FakeMailHost::new();

    let state = reload_mail_tls(&host, distro(), VERSION_ID).unwrap();

    assert!(state.daemons.iter().all(|daemon| !daemon.active));
    assert!(
        host.spawns()
            .iter()
            .all(|spawn| spawn.argv.get(1).map(String::as_str) != Some("reload-or-restart"))
    );
}

#[test]
fn a_unit_the_service_manager_refuses_is_named() {
    let host = FakeMailHost::new()
        .with_running_unit("dovecot")
        .with_refusing_unit("dovecot");

    let refused = reload_mail_tls(&host, distro(), VERSION_ID).unwrap_err();

    assert_eq!(
        refused,
        MailError::ServiceRefused {
            unit: "dovecot".to_owned()
        }
    );
}

#[test]
fn a_daemon_that_comes_back_without_its_ports_is_not_listening() {
    // The shape a certificate whose key does not match produces: the unit is up
    // and the sockets never bind.
    let host = FakeMailHost::new()
        .with_running_unit("dovecot")
        .with_silent_unit("dovecot");

    let refused = reload_mail_tls(&host, distro(), VERSION_ID).unwrap_err();

    assert_eq!(refused, MailError::NotListening { port: 143 });
}
