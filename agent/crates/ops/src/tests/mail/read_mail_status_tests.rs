//! The whole status read, for the status rpc.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::mail::fake_mail_host::{FakeMailHost, VERSION_ID, distro};
use crate::mail::mail_error::MailError;
use crate::mail::read_mail_status::read_mail_status;

#[test]
fn a_host_where_mail_was_never_enabled_answers_a_state_and_not_an_error() {
    let observed = read_mail_status(&FakeMailHost::new(), distro(), VERSION_ID).unwrap();

    assert_eq!(observed.state.daemons.len(), 4);
    assert!(observed.state.daemons.iter().all(|daemon| !daemon.active));
    assert_eq!(observed.details.queue_messages, None);
}

#[test]
fn the_state_and_the_readings_describe_the_same_host() {
    let host = FakeMailHost::new()
        .with_running_unit(distro().postfix_service())
        .with_tool_outcome(&distro().postqueue_binary(), &["-j"], 0, "{}\n{}\n");

    let observed = read_mail_status(&host, distro(), VERSION_ID).unwrap();

    assert!(observed.state.daemons.iter().any(|daemon| daemon.active));
    assert_eq!(observed.details.queue_messages, Some(2));
}

#[test]
fn a_release_the_adapter_does_not_know_is_refused_before_anything_is_asked() {
    let host = FakeMailHost::new();

    let error = read_mail_status(&host, distro(), "no-such-release").unwrap_err();

    assert_eq!(error, MailError::UnsupportedRelease);
    assert!(host.spawns().is_empty());
}

#[test]
fn an_unreadable_socket_table_is_an_error_and_not_an_empty_status() {
    let host = FakeMailHost::new().with_unreadable_sockets();

    let error = read_mail_status(&host, distro(), VERSION_ID).unwrap_err();

    assert_eq!(error, MailError::SocketsUnreadable);
}
