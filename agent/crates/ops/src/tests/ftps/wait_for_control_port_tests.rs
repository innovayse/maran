//! Waiting a bounded time for a restarted daemon to answer its control port.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::call_class::CallClass;

use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro};
use crate::ftps::ftps_host::FtpsHost;
use crate::ftps::model::ftps_unit::FtpsUnit;
use crate::ftps::wait_for_control_port::wait_for_control_port;

/// A host whose daemon is up and, from the clock's start, answers `delay` later.
fn restarted_host(delay: Duration) -> FakeFtpsHost {
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .answering_after(delay);
    host.run(
        &distro().service_manager(),
        &FtpsUnit::RESTART,
        CallClass::Reload,
    )
    .unwrap();
    host
}

#[test]
fn a_port_that_answers_at_once_costs_one_probe_and_no_pause() {
    let host = restarted_host(Duration::ZERO);

    assert!(wait_for_control_port(&host));
    assert_eq!(host.probes(), 1);
    assert!(host.pauses().is_empty());
}

#[test]
fn a_port_that_answers_after_sixty_milliseconds_is_found_on_the_second_probe() {
    let host = restarted_host(Duration::from_millis(60));

    assert!(wait_for_control_port(&host));
    assert_eq!(host.probes(), 2);
    assert_eq!(host.pauses(), vec![Duration::from_millis(100)]);
}

#[test]
fn a_greeting_that_is_not_a_220_is_not_an_answer() {
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .greeting_of("421 Service not available\r\n");

    assert!(!wait_for_control_port(&host));
}

#[test]
fn a_silent_port_is_given_up_on_at_the_deadline_in_hundred_millisecond_steps() {
    let host = FakeFtpsHost::with_certificate()
        .with_running_daemon()
        .silent_on_control_port();

    assert!(!wait_for_control_port(&host));
    assert_eq!(host.elapsed(), Duration::from_secs(10));
    assert_eq!(host.pauses().len(), 100);
    assert_eq!(host.probes(), 101);
}

#[test]
fn a_port_that_answers_just_inside_the_deadline_is_still_found() {
    let host = restarted_host(Duration::from_millis(9_900));

    assert!(wait_for_control_port(&host));
}
