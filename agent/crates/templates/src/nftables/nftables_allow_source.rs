//! Where an accept rule lets traffic in from.

use crate::nftables::nftables_network::NftablesNetwork;

/// The sources an accept rule admits: every source, or exactly one network.
///
/// One value and not an "open to everyone" flag beside a network string that
/// is empty in one of the two states: "open to the world" is a decision the
/// caller states by choosing [`Self::Anyone`], never one an empty string falls
/// into, and a network cannot be both present and unused.
pub enum NftablesAllowSource {
    /// Every source: the rule renders without a `saddr` clause at all.
    Anyone,
    /// One network: the rule renders `<family_keyword> saddr <cidr>`.
    Network(NftablesNetwork),
}

impl NftablesAllowSource {
    /// The restricting network, or `None` when the rule is open to every
    /// source. What the ruleset template branches on.
    #[must_use]
    pub fn network(&self) -> Option<&NftablesNetwork> {
        match self {
            Self::Anyone => None,
            Self::Network(network) => Some(network),
        }
    }
}
