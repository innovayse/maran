//! The state read from the machine, not from what was asked for.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, HOSTNAME, VERSION_ID, distro};
use crate::mail::get_mail_status::get_mail_status;
use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::mail::model::mail_layout::MailLayout;
use crate::mail::model::port_binding::PortBinding;

fn layout() -> MailLayout {
    MailLayout::resolve(distro(), VERSION_ID).unwrap()
}

#[test]
fn a_host_where_mail_was_never_enabled_reports_every_daemon_stopped_and_every_port_unbound() {
    let host = FakeMailHost::new();

    let state = get_mail_status(&host, distro(), &layout()).unwrap();

    assert_eq!(state.daemons.len(), 4);
    assert!(
        state
            .daemons
            .iter()
            .all(|daemon| daemon.installed && !daemon.active)
    );
    assert!(
        state
            .ports
            .iter()
            .all(|port| port.binding == PortBinding::NotBound)
    );
}

#[test]
fn the_daemons_are_reported_in_start_order() {
    let state = get_mail_status(&FakeMailHost::new(), distro(), &layout()).unwrap();
    let order: Vec<MailDaemon> = state.daemons.iter().map(|daemon| daemon.daemon).collect();

    assert_eq!(order, MailDaemon::START_ORDER);
}

#[test]
fn a_daemon_whose_program_is_missing_reads_as_not_installed_and_not_as_stopped() {
    let host = FakeMailHost::new().without_rspamd();

    let state = get_mail_status(&host, distro(), &layout()).unwrap();

    let rspamd = state
        .daemons
        .iter()
        .find(|d| d.daemon == MailDaemon::Rspamd)
        .unwrap();
    assert!(!rspamd.installed);
    let postfix = state
        .daemons
        .iter()
        .find(|d| d.daemon == MailDaemon::Postfix)
        .unwrap();
    assert!(postfix.installed);
}

#[test]
fn running_units_show_their_bound_ports() {
    let host = FakeMailHost::new()
        .with_running_unit("postfix")
        .with_running_unit("dovecot");

    let state = get_mail_status(&host, distro(), &layout()).unwrap();

    let public: Vec<u16> = state
        .ports
        .iter()
        .filter(|port| port.binding == PortBinding::Public)
        .map(|port| port.port)
        .collect();
    assert_eq!(public, [25, 465, 587, 143, 993]);
}

#[test]
fn an_active_unit_that_binds_nothing_is_active_and_its_ports_are_still_unbound() {
    // The status asks the kernel and the service manager separately, so a unit
    // that is up and silent shows as exactly that.
    let host = FakeMailHost::new()
        .with_running_unit("postfix")
        .with_silent_unit("postfix");

    let state = get_mail_status(&host, distro(), &layout()).unwrap();

    let postfix = state
        .daemons
        .iter()
        .find(|d| d.daemon == MailDaemon::Postfix)
        .unwrap();
    assert!(postfix.active);
    assert!(
        state
            .ports
            .iter()
            .all(|port| port.binding == PortBinding::NotBound)
    );
}

#[test]
fn the_hostname_and_the_tls_setting_are_read_back_from_postfix() {
    let state = get_mail_status(&FakeMailHost::new(), distro(), &layout()).unwrap();

    assert_eq!(state.effective_hostname.as_deref(), Some(HOSTNAME));
    assert_eq!(state.tls_configured, Some(true));
}

#[test]
fn an_unreadable_socket_table_is_an_error_and_never_an_empty_answer() {
    let host = FakeMailHost::new().with_unreadable_sockets();

    assert_eq!(
        get_mail_status(&host, distro(), &layout()).unwrap_err(),
        MailError::SocketsUnreadable
    );
}
