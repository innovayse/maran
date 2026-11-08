//! Turning mail off and keeping it off.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::disable_mail::disable_mail;
use crate::mail::enable_mail::enable_mail;
use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro, settings};
use crate::mail::model::port_binding::PortBinding;

#[test]
fn disabling_an_enabled_stack_stops_disables_and_masks_every_unit_and_closes_the_ports() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();

    let state = disable_mail(&host, distro(), VERSION_ID).unwrap();

    for unit in host.all_units() {
        assert!(!host.is_active(&unit), "{unit}");
        assert!(!host.is_enabled(&unit), "{unit}");
        assert!(host.is_masked(&unit), "{unit}");
    }
    assert!(state.daemons.iter().all(|daemon| !daemon.active));
    assert!(
        state
            .ports
            .iter()
            .all(|port| port.binding == PortBinding::NotBound)
    );
}

#[test]
fn disabling_leaves_the_configuration_exactly_where_it_is() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();
    let main = host.file("/etc/postfix/main.cf");
    let writes = host.writes().len();

    disable_mail(&host, distro(), VERSION_ID).unwrap();

    assert!(main.is_some());
    assert_eq!(host.file("/etc/postfix/main.cf"), main);
    assert_eq!(host.writes().len(), writes);
}

#[test]
fn disabling_a_stack_that_is_already_off_converges() {
    let host = FakeMailHost::new();

    let state = disable_mail(&host, distro(), VERSION_ID).unwrap();

    assert!(state.daemons.iter().all(|daemon| !daemon.active));
}

#[test]
fn the_smtp_server_is_stopped_before_the_daemons_it_depends_on() {
    let host = FakeMailHost::new();
    enable_mail(&host, distro(), VERSION_ID, &settings()).unwrap();
    let before = host.spawns().len();

    disable_mail(&host, distro(), VERSION_ID).unwrap();

    let stopped: Vec<String> = host.spawns()[before..]
        .iter()
        .filter(|spawn| spawn.argv.get(1).map(String::as_str) == Some("disable"))
        .map(|spawn| spawn.argv.last().unwrap().clone())
        .collect();
    assert_eq!(stopped, ["postfix", "dovecot", "rspamd", "redis-server"]);
}

#[test]
fn a_release_the_adapter_does_not_know_is_refused_and_nothing_is_stopped() {
    let host = FakeMailHost::new();

    assert!(disable_mail(&host, distro(), "11").is_err());
    assert!(host.spawns().is_empty());
}
