//! What a clock-synchronisation reading looks like on the wire.

use maran_ops::monitor::ClockSyncState;

use crate::proto::{
    AgentError, ClockSyncState as WireState, GetClockSyncOk, get_clock_sync_response,
};

/// Maps one `GetClockSync` outcome onto the response's `oneof`.
///
/// A reading the agent could not take travels as the error arm, never as a
/// state: "could not find out" and "not synchronised" are different answers,
/// and the panel alerts on the second only. `ClockSyncState` is
/// `#[non_exhaustive]`, so a future variant answers `Unspecified` — which the
/// panel reads as no answer — rather than failing to build.
#[must_use]
pub fn reported_clock_sync(
    outcome: Result<ClockSyncState, AgentError>,
) -> get_clock_sync_response::Result {
    let state = match outcome {
        Ok(ClockSyncState::Synchronized) => WireState::Synchronized,
        Ok(ClockSyncState::NotSynchronized) => WireState::NotSynchronized,
        Ok(ClockSyncState::SyncServiceDisabled) => WireState::SyncServiceDisabled,
        Ok(_) => WireState::Unspecified,
        Err(error) => return get_clock_sync_response::Result::Error(error),
    };

    get_clock_sync_response::Result::Ok(GetClockSyncOk {
        state: state as i32,
    })
}

#[cfg(test)]
#[path = "../../tests/services/monitor/reported_clock_sync_tests.rs"]
mod tests;
