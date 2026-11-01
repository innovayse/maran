//! Tests for how a clock-synchronisation reading reaches the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::monitor::ClockSyncState;

use super::reported_clock_sync;
use crate::proto::{AgentError, ClockSyncState as WireState, ErrorCode, get_clock_sync_response};

/// The wire state a successful reading of `state` carries.
fn wire_state(state: ClockSyncState) -> WireState {
    match reported_clock_sync(Ok(state)) {
        get_clock_sync_response::Result::Ok(ok) => WireState::try_from(ok.state).unwrap(),
        get_clock_sync_response::Result::Error(error) => panic!("unexpected error {error:?}"),
    }
}

#[test]
fn each_state_maps_onto_its_own_wire_value() {
    assert_eq!(wire_state(ClockSyncState::Synchronized), WireState::Synchronized);
    assert_eq!(
        wire_state(ClockSyncState::NotSynchronized),
        WireState::NotSynchronized
    );
    assert_eq!(
        wire_state(ClockSyncState::SyncServiceDisabled),
        WireState::SyncServiceDisabled
    );
}

/// A reading the agent could not take is the error arm, never a state the
/// panel would alert on.
#[test]
fn a_failed_reading_travels_as_the_error_and_not_as_a_state() {
    let error = AgentError {
        code: ErrorCode::SystemFailure as i32,
        message: String::from("the clock synchronisation state could not be read"),
        tool_output: String::new(),
    };

    let result = reported_clock_sync(Err(error.clone()));

    assert_eq!(result, get_clock_sync_response::Result::Error(error));
}
