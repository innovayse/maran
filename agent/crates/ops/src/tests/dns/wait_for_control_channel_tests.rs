//! Waiting, for a bounded time, for a freshly restarted DNS daemon to answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use crate::dns::fake_dns_host::{FakeDnsHost, distro};
use crate::dns::wait_for_control_channel::wait_for_control_channel;

#[test]
fn a_daemon_that_answers_at_once_is_not_waited_for() {
    let host = FakeDnsHost::new().with_running_daemon();

    assert!(wait_for_control_channel(&host, distro()).unwrap());
    assert!(host.pauses().is_empty());
}

#[test]
fn a_daemon_that_never_answers_is_given_up_on_after_the_deadline_in_virtual_time() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_silent_daemon();

    assert!(!wait_for_control_channel(&host, distro()).unwrap());
    let waited: Duration = host.pauses().iter().sum();
    assert!(
        waited >= Duration::from_secs(10),
        "gave up after {waited:?}"
    );
    assert!(waited < Duration::from_secs(11), "waited {waited:?}");
}

#[test]
fn a_unit_that_is_not_running_never_answers() {
    let host = FakeDnsHost::new();

    assert!(!wait_for_control_channel(&host, distro()).unwrap());
}
