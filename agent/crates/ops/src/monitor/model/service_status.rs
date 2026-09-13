//! What the agent reports about one managed unit.

use crate::monitor::model::service_state::ServiceState;

/// One managed unit's state, with the words the agent based it on.
///
/// `detail` exists because [`ServiceState`] deliberately collapses several
/// situations into [`ServiceState::Unknown`], and an operator looking at the
/// panel needs to know which one: "waiting for its socket" and "not installed
/// on this host" call for very different actions.
///
/// It carries systemd's own vocabulary — its `ActiveState` and `SubState`
/// words, or a short sentence this crate wrote — and never a tool's standard
/// error. The distinction matters: unit names come from the closed set on the
/// `DistroAdapter`, so nothing a caller supplies reaches the service manager,
/// and nothing the service manager printed about a caller's input can come back
/// through this field.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ServiceStatus {
    /// The unit as the `DistroAdapter` names it, in the order that trait
    /// fixes.
    pub unit: String,
    /// Up, down, or not known.
    pub state: ServiceState,
    /// Why, in a few words an operator can act on.
    pub detail: String,
}
