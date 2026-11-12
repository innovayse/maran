//! What a refresh of the output chain's address sets found and did.

/// What `refresh_self_sets` found and did.
///
/// A caller that follows the host's addresses records the host as followed ONLY for
/// [`Self::Refreshed`] and [`Self::Repaired`]; the others wrote nothing, or wrote for a host
/// that is no longer the one it was asked about, and must be asked again.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SelfSetsOutcome {
    /// The host has no ruleset file yet (the installer seeds it); nothing was written.
    /// The startup reconcile owns that.
    NotSeeded,
    /// The kernel has no `self_v4` set (the firewall is not loaded, or predates the output
    /// chain); nothing was written. The startup reconcile owns that.
    NotLoaded,
    /// The ruleset file and both sets in the kernel were brought to the host's present
    /// addresses; no rule was reloaded.
    Refreshed,
    /// More than the sets was stale (the chain was seeded inert because the host's facts were
    /// unreadable then, or the uid floor moved): the whole ruleset was re-rendered, written and
    /// loaded as a rule change does.
    Repaired,
    /// The host's addresses changed again between the reading the caller acted on and the lock:
    /// the file and the kernel were brought to the newer reading, and the caller's reading is
    /// not the one they hold.
    HostMoved,
}
