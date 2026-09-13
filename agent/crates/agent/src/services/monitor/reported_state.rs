//! What a unit's state looks like on the wire, in both of its spellings.

use maran_ops::monitor::ServiceState;

use crate::proto::ServiceState as WireState;

/// The tri-state and the legacy boolean a status row carries.
///
/// Both, from one place, because `ServiceStatus` carries both and they must not
/// be able to disagree: `state` is what a reader should prefer, and `running`
/// is kept so a panel built against the two-value contract keeps working across
/// one release (rules/proto.md, additive evolution).
///
/// **The boolean is true only for `Running`.** `Stopped` and `Unknown` both
/// report false, and that conflation is exactly why the enum was added: a
/// socket-activated service nothing has connected to yet is not down, but the
/// old field has no way to say so. The direction of the collapse is the safe
/// one — an old reader shows "not running" for a unit the agent is unsure
/// about, rather than showing a green tick for one it has no reading for.
#[must_use]
pub fn reported_state(state: ServiceState) -> (WireState, bool) {
    match state {
        ServiceState::Running => (WireState::Running, true),
        ServiceState::Stopped => (WireState::Stopped, false),
        ServiceState::Unknown => (WireState::Unknown, false),
    }
}

#[cfg(test)]
#[path = "../../tests/services/monitor/reported_state_tests.rs"]
mod tests;
