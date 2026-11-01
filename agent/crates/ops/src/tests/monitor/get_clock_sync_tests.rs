//! Tests for `get_clock_sync`. Mirrors the source tree (rules/testing.md).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;

use super::get_clock_sync;
use crate::monitor::fake_monitor_host::{FakeMonitorHost, distro, rhel_distro};
use crate::monitor::model::clock_sync_state::ClockSyncState;
use crate::monitor::monitor_error::MonitorError;

/// The fake answers a run by its LAST argument, which for this operation is
/// the second property it asks for.
const LAST_ARGUMENT: &str = "--property=NTPSynchronized";

/// A host whose `timedatectl show` prints `answer`.
fn host_printing(answer: &str) -> FakeMonitorHost {
    FakeMonitorHost::from_ubuntu_captures().with_unit(LAST_ARGUMENT, answer)
}

#[test]
fn a_synchronised_kernel_clock_is_reported_synchronised() {
    let host = host_printing("NTP=yes\nNTPSynchronized=yes\n");

    let state = get_clock_sync(&host, distro()).expect("the read succeeds");

    assert_eq!(state, ClockSyncState::Synchronized);
}

/// The kernel's verdict decides even when timedated knows no sync unit: a
/// daemon it does not list can still discipline the clock.
#[test]
fn a_synchronised_clock_with_no_known_sync_service_is_still_synchronised() {
    let host = host_printing("NTP=no\nNTPSynchronized=yes\n");

    let state = get_clock_sync(&host, distro()).expect("the read succeeds");

    assert_eq!(state, ClockSyncState::Synchronized);
}

#[test]
fn an_enabled_service_with_an_unsynchronised_clock_is_not_synchronised() {
    let host = host_printing("NTP=yes\nNTPSynchronized=no\n");

    let state = get_clock_sync(&host, distro()).expect("the read succeeds");

    assert_eq!(state, ClockSyncState::NotSynchronized);
}

#[test]
fn no_service_and_an_unsynchronised_clock_is_a_disabled_sync_service() {
    let host = host_printing("NTPSynchronized=no\nNTP=no\n");

    let state = get_clock_sync(&host, distro()).expect("the read succeeds");

    assert_eq!(state, ClockSyncState::SyncServiceDisabled);
}

/// A repeated key resolves to its last line, as every key-value reading here does.
#[test]
fn the_last_line_naming_a_property_wins() {
    let host = host_printing("NTP=yes\nNTPSynchronized=yes\nNTPSynchronized=no\n");

    let state = get_clock_sync(&host, distro()).expect("the read succeeds");

    assert_eq!(state, ClockSyncState::NotSynchronized);
}

#[test]
fn a_missing_property_is_an_error_not_a_finding() {
    let host = host_printing("NTP=yes\n");

    let result = get_clock_sync(&host, distro());

    assert_eq!(result, Err(MonitorError::ClockStateUnavailable));
}

#[test]
fn a_value_other_than_yes_or_no_is_an_error_not_a_finding() {
    let host = host_printing("NTP=yes\nNTPSynchronized=maybe\n");

    let result = get_clock_sync(&host, distro());

    assert_eq!(result, Err(MonitorError::ClockStateUnavailable));
}

#[test]
fn a_non_zero_exit_is_an_error_not_a_finding() {
    let host = host_printing("NTP=yes\nNTPSynchronized=no\n").with_service_manager_status(1);

    let result = get_clock_sync(&host, distro());

    assert_eq!(result, Err(MonitorError::ClockStateUnavailable));
}

#[test]
fn a_program_that_cannot_start_is_reported_as_such() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_absent_service_manager();

    let result = get_clock_sync(&host, distro());

    assert_eq!(result, Err(MonitorError::program_unavailable()));
}

#[test]
fn a_program_killed_at_its_deadline_is_a_bound_not_a_finding() {
    let host = FakeMonitorHost::from_ubuntu_captures().with_timed_out_service_manager();

    let result = get_clock_sync(&host, distro());

    assert!(matches!(result, Err(MonitorError::Bound(_))), "{result:?}");
}

/// The exact argv: a reporting subcommand, both properties, the query class,
/// and the adapter's absolute path on both families.
#[test]
fn timedatectl_is_a_query_asking_for_both_properties() {
    for adapter in [distro(), rhel_distro()] {
        let host = host_printing("NTP=yes\nNTPSynchronized=yes\n");

        get_clock_sync(&host, adapter).expect("the read succeeds");

        assert_eq!(
            host.commands(),
            vec![vec![
                String::from("/usr/bin/timedatectl"),
                String::from("show"),
                String::from("--property=NTP"),
                String::from(LAST_ARGUMENT),
            ]]
        );
        assert_eq!(host.classes(), vec![CallClass::Query]);
    }
}
