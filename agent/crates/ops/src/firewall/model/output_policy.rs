//! What the ruleset's output chain needs to know about this host.

use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;

use crate::firewall::model::self_address_sets::SelfAddressSets;
use maran_agent_core::utils::login_defs_floor::DEFAULT_ID_FLOOR;

/// The two host facts the ruleset's output chain (issue #531) is rendered from:
/// where the customer uid range starts, and which addresses are this host's
/// own.
///
/// **It is not part of the stored rules.** The ruleset file is the rule store
/// for what an operator authored; these two values are read from the host each
/// time a file is rendered, so a changed `login.defs` or a new address reaches
/// the file on the next apply. Reading the file back masks them (see
/// [`RulesetState::parse`](crate::firewall::model::ruleset_state::RulesetState::parse)),
/// which is what lets an address that changed since the last apply stay a file
/// this agent can still read.
///
/// The addresses are a [`SelfAddressSets`], which drops loopback, keeps no entry
/// inside another and sorts the rest (see there).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OutputPolicy {
    /// The first uid of the customer range.
    customer_uid_min: u32,
    /// The host's own destinations.
    sets: SelfAddressSets,
}

impl OutputPolicy {
    /// The policy of a host whose facts could not be read: the default uid floor and
    /// no address at all, which renders an output chain that matches nothing. Used
    /// only where the alternative is no firewall (the installer's seed); the
    /// start-up pass replaces it with the real facts.
    #[must_use]
    pub fn inert() -> Self {
        Self::new(DEFAULT_ID_FLOOR, [])
    }

    /// The policy for a host whose customer range starts at `customer_uid_min`
    /// and which answers on `addresses` (and on no routed range).
    #[must_use]
    pub fn new(customer_uid_min: u32, addresses: impl IntoIterator<Item = IpAddr>) -> Self {
        Self::for_host(
            customer_uid_min,
            &HostAddresses {
                addresses: addresses.into_iter().collect(),
                local_prefixes: Vec::new(),
            },
        )
    }

    /// The policy for a host whose customer range starts at `customer_uid_min`
    /// and which answers on what `host` says.
    #[must_use]
    pub fn for_host(customer_uid_min: u32, host: &HostAddresses) -> Self {
        Self {
            customer_uid_min,
            sets: SelfAddressSets::new(host),
        }
    }

    /// The first uid of the customer range.
    #[must_use]
    pub fn customer_uid_min(&self) -> u32 {
        self.customer_uid_min
    }

    /// The host's own IPv4 addresses, without loopback, in ascending order.
    #[must_use]
    pub fn self_v4(&self) -> &[Ipv4Addr] {
        self.sets.v4()
    }

    /// The host's own local IPv4 ranges, without loopback, in ascending order.
    #[must_use]
    pub fn self_v4_prefixes(&self) -> &[Ipv4Prefix] {
        self.sets.v4_prefixes()
    }

    /// The host's own IPv6 addresses, without `::1`, in ascending order.
    #[must_use]
    pub fn self_v6(&self) -> &[Ipv6Addr] {
        self.sets.v6()
    }
}
