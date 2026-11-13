//! The destinations the output chain of the port-25 lockdown calls "this host".

use std::collections::BTreeSet;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;

/// The contents of the two address sets (`self_v4`, `self_v6`) of `table inet
/// maran`: everything a connection can name to reach this host itself, loopback
/// aside.
///
/// Built from one [`HostAddresses`] reading, by the one function every writer of
/// the sets goes through (the full ruleset render and the refresh of the sets
/// alone), so the two cannot disagree about what is in them.
///
/// **Loopback is not carried.** The templates write `127.0.0.0/8` and `::1`
/// themselves; an entry inside them, or a range around them, would overlap an
/// interval and `nft` would refuse the whole file or script. [`Self::new`] drops
/// them, together with the unspecified address (never a destination).
///
/// **No entry overlaps another.** The IPv4 set holds intervals and `nft` refuses
/// two that overlap, so a local range wider than another is kept and the narrower
/// one is dropped, and an address inside a kept range is dropped: the range
/// already matches it. What remains is sorted and de-duplicated so that the same
/// host renders the same bytes every time. (A local range that contains the
/// loopback range cannot sit beside the constant and is dropped; a route
/// `local 0.0.0.0/0` is a host that answers everywhere and is out of this
/// lockdown's reach.)
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SelfAddressSets {
    /// The local IPv4 ranges, without loopback, none inside another.
    v4_prefixes: Vec<Ipv4Prefix>,
    /// The IPv4 addresses, without loopback and none inside a range.
    v4: Vec<Ipv4Addr>,
    /// The IPv6 addresses, without `::1`.
    v6: Vec<Ipv6Addr>,
}

impl SelfAddressSets {
    /// The sets for a host that carries `host`.
    #[must_use]
    pub fn new(host: &HostAddresses) -> Self {
        // The loopback filter is the one the mail server's table uses too, so the
        // two layers cannot disagree about which ranges are "this host".
        let candidates: BTreeSet<Ipv4Prefix> = host.ranges_beside_loopback().into_iter().collect();
        let v4_prefixes: Vec<Ipv4Prefix> = candidates
            .iter()
            .copied()
            .filter(|prefix| {
                !candidates
                    .iter()
                    .any(|other| other != prefix && other.covers(prefix))
            })
            .collect();

        let mut v4 = BTreeSet::new();
        let mut v6 = BTreeSet::new();
        for address in host.addresses_beside_loopback() {
            match address {
                IpAddr::V4(address) => {
                    if !v4_prefixes.iter().any(|prefix| prefix.contains(address)) {
                        v4.insert(address);
                    }
                }
                IpAddr::V6(address) => {
                    v6.insert(address);
                }
            }
        }

        Self {
            v4_prefixes,
            v4: v4.into_iter().collect(),
            v6: v6.into_iter().collect(),
        }
    }

    /// The local IPv4 ranges, in ascending order.
    #[must_use]
    pub fn v4_prefixes(&self) -> &[Ipv4Prefix] {
        &self.v4_prefixes
    }

    /// The IPv4 addresses, in ascending order.
    #[must_use]
    pub fn v4(&self) -> &[Ipv4Addr] {
        &self.v4
    }

    /// The IPv6 addresses, in ascending order.
    #[must_use]
    pub fn v6(&self) -> &[Ipv6Addr] {
        &self.v6
    }
}

#[cfg(test)]
#[path = "../../tests/firewall/self_address_sets_tests.rs"]
mod tests;
