//! One address the firewall is currently dropping.

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;

/// A ban that is in force right now, as the kernel reports it.
///
/// The agent is not the durable store of a ban and this type reflects that:
/// there is no reason, no operator, and no creation time on it, because the
/// panel records all three and the agent records none (R6). A reason once
/// travelled on the wire and into an `nft` comment, which was an injection
/// primitive — `nft` parses its arguments in its own grammar — and the field
/// is now panel metadata only.
///
/// The remaining time is what the kernel is counting down, not what the ban
/// was created with. Both families' nftables units flush the ruleset on stop
/// and reload, so a runtime ban does not survive a service restart or a
/// reboot; the panel's own reconciler re-applies the unexpired ones, and it
/// needs to know how much of each is left rather than how long each was
/// originally for.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ActiveBan {
    /// The banned address.
    pub address: BanAddress,
    /// How much longer the kernel will hold the ban, or `None` when it holds
    /// it until somebody removes it.
    pub expires_in: Option<Duration>,
}
