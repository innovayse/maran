//! Tests for the tri-state and the legacy boolean a status row carries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::monitor::ServiceState;

use super::reported_state;
use crate::proto::ServiceState as WireState;

#[test]
fn a_running_unit_is_running_in_both_spellings() {
    assert_eq!(
        reported_state(ServiceState::Running),
        (WireState::Running, true)
    );
}

#[test]
fn a_stopped_unit_is_stopped_and_the_legacy_boolean_agrees() {
    assert_eq!(
        reported_state(ServiceState::Stopped),
        (WireState::Stopped, false)
    );
}

#[test]
fn a_unit_the_agent_cannot_call_is_unknown_and_never_stopped() {
    // The whole reason the enum was added. A socket-activated service nothing
    // has connected to yet is not down — and on the Debian family that is every
    // healthy host from boot until the first SSH login. Collapsing it to
    // STOPPED invents an outage the panel's alerting would mail somebody about.
    let (state, running) = reported_state(ServiceState::Unknown);

    assert_eq!(state, WireState::Unknown);
    assert_ne!(state, WireState::Stopped);
    // The legacy field cannot say "unknown" at all, so it says false. That
    // direction is the safe one: an old reader shows "not running" for a unit
    // the agent is unsure about, rather than a green tick for one it has no
    // reading for.
    assert!(!running);
}

#[test]
fn no_state_is_ever_reported_as_the_unspecified_value() {
    // SERVICE_STATE_UNSPECIFIED is what an agent that predates the field sends,
    // and `monitor.proto` tells readers to fall back to the boolean when they
    // see it. A live agent emitting it would send every reader down that path
    // and lose the tri-state entirely.
    for state in [
        ServiceState::Running,
        ServiceState::Stopped,
        ServiceState::Unknown,
    ] {
        assert_ne!(
            reported_state(state).0,
            WireState::Unspecified,
            "{state:?} must be reported as one of the three states"
        );
    }
}
