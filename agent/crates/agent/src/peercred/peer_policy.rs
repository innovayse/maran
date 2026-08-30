//! Which unix uid may use the agent at all.

/// Decides which unix uid may use the agent.
///
/// The agent runs as root and its command set is the whole attack surface of the
/// panel, so authorisation starts below the RPC layer: a caller that is not the
/// panel user never reaches a handler, whatever it asks for.
#[derive(Debug, Clone, Copy)]
pub struct PeerPolicy {
    /// The single uid allowed to connect — the `panel` user in production.
    allow_uid: u32,
}

impl PeerPolicy {
    /// Creates a policy allowing exactly `allow_uid`.
    #[must_use]
    pub fn new(allow_uid: u32) -> Self {
        Self { allow_uid }
    }

    /// Whether `peer_uid` may use the agent.
    ///
    /// One uid, with no special case for root: an allow-list of exactly one is
    /// the only rule that cannot be widened by accident, and a root process that
    /// wants the agent's help can ask as the panel user.
    #[must_use]
    pub fn permits(&self, peer_uid: u32) -> bool {
        peer_uid == self.allow_uid
    }
}

#[cfg(test)]
#[path = "../tests/peercred/peer_policy_tests.rs"]
mod tests;
