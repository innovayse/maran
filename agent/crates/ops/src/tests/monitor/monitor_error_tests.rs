//! How the monitor area's error holds a bound and tells it from the failures it
//! is easy to confuse it with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_distro::PendingUpdatesFailure;

use super::MonitorError;

/// A deadline failure, as the core builds one.
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: String::from("/usr/bin/systemctl"),
        deadline: Duration::from_secs(60),
    }
}

#[test]
fn a_bound_converts_into_the_bound_variant_and_no_other() {
    assert_eq!(
        MonitorError::from(timed_out()),
        MonitorError::Bound(timed_out())
    );
}

#[test]
fn a_bound_names_the_program_and_says_the_kill_is_not_a_rollback() {
    let printed = MonitorError::Bound(timed_out()).to_string();

    assert!(printed.contains("/usr/bin/systemctl"), "{printed}");
    assert!(printed.contains("may still run"), "{printed}");
}

/// A program that was stopped has not said the service manager is missing.
#[test]
fn a_bound_is_not_a_program_that_never_ran() {
    assert_ne!(
        MonitorError::Bound(timed_out()),
        MonitorError::program_unavailable()
    );
}

/// An unreadable clock answer is its own failure, not a service manager that never ran.
#[test]
fn an_unreadable_clock_state_is_not_a_program_that_never_ran() {
    assert_ne!(
        MonitorError::ClockStateUnavailable,
        MonitorError::program_unavailable()
    );
    assert!(
        MonitorError::ClockStateUnavailable
            .to_string()
            .contains("clock synchronisation")
    );
}

/// An unreadable package catalogue is its own failure: neither a bound nor a
/// service manager that never ran, and its text names the reason and nothing the
/// package manager printed.
#[test]
fn an_unreadable_update_catalogue_is_its_own_failure_and_names_only_its_reason() {
    let error = MonitorError::UpdateCatalogueUnavailable {
        reason: PendingUpdatesFailure::Unsuccessful { status: 1 },
    };

    assert_ne!(error, MonitorError::Bound(timed_out()));
    assert_ne!(error, MonitorError::program_unavailable());
    assert_eq!(
        error.to_string(),
        "the pending package updates could not be read: the package manager exited with status 1"
    );
}
