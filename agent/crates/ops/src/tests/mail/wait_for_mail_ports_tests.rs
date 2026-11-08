//! Waiting for the daemons to bind, in virtual time.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, distro};
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;
use crate::mail::model::mail_ports::MailPorts;
use crate::mail::wait_for_mail_ports::wait_for_mail_ports;

fn running_host() -> FakeMailHost {
    FakeMailHost::new()
        .with_running_unit(distro().postfix_service())
        .with_running_unit(distro().dovecot_service())
}

#[test]
fn ports_that_are_already_bound_end_the_wait_at_once() {
    let host = running_host();

    wait_for_mail_ports(&host, &MailPorts::expected(false)).unwrap();

    assert_eq!(host.monotonic_now().as_millis(), 0);
}

#[test]
fn a_port_nothing_binds_is_named_after_the_whole_deadline() {
    let host = FakeMailHost::new().with_running_unit(distro().postfix_service());

    let refused = wait_for_mail_ports(&host, &MailPorts::expected(false)).unwrap_err();

    // Postfix binds 25, 465, 587; IMAP is the first port nobody holds.
    assert_eq!(refused, MailError::NotListening { port: 143 });
    assert!(host.monotonic_now().as_secs() >= 20);
}

#[test]
fn the_pop3_ports_are_expected_only_when_pop3_is_on() {
    let host = running_host();

    wait_for_mail_ports(&host, &MailPorts::expected(false)).unwrap();
    let refused = wait_for_mail_ports(&host, &MailPorts::expected(true)).unwrap_err();

    assert_eq!(refused, MailError::NotListening { port: 110 });
}

#[test]
fn an_unreadable_socket_table_is_an_error_and_never_a_wait() {
    let host = running_host().with_unreadable_sockets();

    assert_eq!(
        wait_for_mail_ports(&host, &MailPorts::expected(false)).unwrap_err(),
        MailError::SocketsUnreadable
    );
}
