//! Turning mail on: refusals before anything changes, the order of the start, and
//! what a failure leaves behind.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::enable_mail::enable_mail;
use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro, settings, settings_with_pop3};
use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::port_binding::PortBinding;

#[test]
fn enabling_on_a_fresh_host_starts_all_four_daemons_and_binds_the_mail_ports() {
    let host = FakeMailHost::new();

    let state = enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();

    assert!(state.daemons.iter().all(|daemon| daemon.active));
    for unit in host.all_units() {
        assert!(!host.is_masked(&unit), "{unit}");
        assert!(host.is_enabled(&unit), "{unit}");
    }
    let public: Vec<u16> = state
        .ports
        .iter()
        .filter(|port| port.binding == PortBinding::Public)
        .map(|port| port.port)
        .collect();
    assert_eq!(public, [25, 465, 587, 143, 993]);
}

#[test]
fn the_daemons_are_started_in_dependency_order() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();

    let starts: Vec<String> = host
        .spawns()
        .into_iter()
        .filter(|spawn| spawn.argv.get(1).map(String::as_str) == Some("reload-or-restart"))
        .map(|spawn| spawn.argv.last().unwrap().clone())
        .collect();

    assert_eq!(starts, ["redis-server", "rspamd", "dovecot", "postfix"]);
}

#[test]
fn nothing_is_written_when_the_hostname_has_no_certificate() {
    let host = FakeMailHost::new().without_certificate();

    let refused = enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap_err();

    assert!(matches!(refused, MailError::CertificateMissing { .. }));
    assert!(host.writes().is_empty());
    assert!(host.spawns().is_empty());
}

#[test]
fn a_daemon_that_is_not_installed_is_named_and_nothing_is_written() {
    let host = FakeMailHost::new().without_rspamd();

    let refused = enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap_err();

    assert_eq!(
        refused,
        MailError::NotInstalled {
            daemon: MailDaemon::Rspamd
        }
    );
    assert!(host.writes().is_empty());
    assert!(host.is_masked("postfix"));
}

#[test]
fn a_release_the_adapter_does_not_know_is_refused_before_the_host_is_touched() {
    let host = FakeMailHost::new();

    let refused = enable_mail(&host, distro(), "11", &settings()).unwrap_err();

    assert_eq!(refused, MailError::UnsupportedRelease);
    assert!(host.spawns().is_empty());
}

#[test]
fn enabling_twice_writes_nothing_and_restarts_nothing_the_second_time() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();
    let writes = host.writes().len();
    let restarts = |host: &FakeMailHost| {
        host.spawns()
            .iter()
            .filter(|spawn| spawn.argv.get(1).map(String::as_str) == Some("reload-or-restart"))
            .count()
    };
    let restarted = restarts(&host);

    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();

    assert_eq!(host.writes().len(), writes);
    assert_eq!(restarts(&host), restarted);
}

#[test]
fn a_changed_setting_rewrites_only_what_it_changes_and_restarts_only_those_daemons() {
    let host = FakeMailHost::new().with_pop3_bound();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();
    let writes = host.writes().len();

    enable_mail(&host, distro(), VERSION_ID, &settings_with_pop3()).unwrap();

    // POP3 is a Dovecot setting and nothing else's.
    let added: Vec<MailDaemon> = host.writes()[writes..]
        .iter()
        .map(|write| write.daemon)
        .collect();
    assert_eq!(added, [MailDaemon::Dovecot]);
}

#[test]
fn pop3_ports_are_waited_for_only_when_pop3_is_on() {
    let without = FakeMailHost::new();
    enable_mail(&without, distro(), VERSION_ID, &settings()).unwrap();

    let silent = FakeMailHost::new();
    let refused = enable_mail(&silent, distro(), VERSION_ID, &settings_with_pop3()).unwrap_err();

    assert_eq!(refused, MailError::NotListening { port: 110 });
}

#[test]
fn a_failed_first_enable_leaves_every_daemon_stopped_and_masked_again() {
    let host = FakeMailHost::new().with_refusing_check(MailDaemon::Dovecot, "bad setting");

    let refused = enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap_err();

    assert!(matches!(
        refused,
        MailError::ConfigRejected {
            daemon: MailDaemon::Dovecot,
            ..
        }
    ));
    for unit in host.all_units() {
        assert!(host.is_masked(&unit), "{unit} was left unmasked");
        assert!(!host.is_active(&unit), "{unit} was left running");
        assert!(!host.is_enabled(&unit), "{unit} was left enabled");
    }
}

#[test]
fn a_refused_change_to_a_running_stack_does_not_stop_it() {
    // The inverse control for the rollback above: stopping a working mail server
    // over a rejected edit would turn the edit into an outage.
    let host = FakeMailHost::new()
        .with_running_unit("postfix")
        .with_refusing_check(MailDaemon::Dovecot, "bad setting");

    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap_err();

    assert!(host.is_active("postfix"));
    assert!(!host.is_masked("postfix"));
}

#[test]
fn a_unit_that_is_up_and_binds_nothing_is_not_listening() {
    let host = FakeMailHost::new().with_silent_unit("postfix");

    let refused = enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap_err();

    assert_eq!(refused, MailError::NotListening { port: 25 });
}

#[test]
fn debian_13_is_configured_with_the_dovecot_2_4_generation() {
    let host = FakeMailHost::new();

    enable_mail(&host, distro(), "13", &settings()).unwrap();

    assert!(
        host.file("/etc/dovecot/dovecot.conf")
            .unwrap()
            .contains("dovecot_config_version = 2.4.0")
    );
}
