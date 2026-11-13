//! What the kernel says this host answers on.

use std::net::{IpAddr, Ipv4Addr};

use crate::utils::ipv4_prefix::Ipv4Prefix;

/// Every address a connection from or to this host can name: the addresses
/// configured on an interface, and the IPv4 ranges the routing table marks local.
///
/// The ranges are a separate field because they are not addresses of an
/// interface: `ip route add local 192.0.2.0/24 dev lo` makes every address of the
/// range answer on this host, and a list of single addresses cannot say so. A
/// layer that listed only the addresses would let a process reach the host's own
/// port 25 through any other address of such a range.
///
/// **Stated limit: IPv6 local ranges.** `ip -6 route add local 2001:db8::/64 dev lo` makes
/// every address of the block answer on this host too, and the reading carries no IPv6
/// range: only the addresses of the interfaces (`/proc/net/if_inet6`). The port-25
/// lockdown therefore does not cover a host that was given such a route by hand; the
/// asymmetry with IPv4 is deliberate, because an IPv6 range needs interval sets in the
/// firewall and a range table in the mail server that neither layer has, and a routed
/// IPv6 block is not something the installer or a hosting setup makes (the addresses a
/// provider routes to a host are configured on an interface, which are read).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct HostAddresses {
    /// The addresses configured on an interface, IPv4 first.
    pub addresses: Vec<IpAddr>,
    /// The IPv4 ranges of type `LOCAL` that are wider than one address. The
    /// loopback range is among them when the kernel lists it.
    pub local_prefixes: Vec<Ipv4Prefix>,
}

/// The loopback network, which both layers of the lockdown write as a constant.
const LOOPBACK_ADDRESS: Ipv4Addr = Ipv4Addr::LOCALHOST;

/// The prefix length of the loopback network.
const LOOPBACK_LENGTH: u8 = 8;

impl HostAddresses {
    /// The local IPv4 ranges the lockdown lists, sorted and without repeats: every
    /// range except the ones that touch the loopback network.
    ///
    /// The ONE filter both layers of the port-25 lockdown (the firewall's address sets and
    /// the mail server's refused-client table) use, so they cannot disagree about which
    /// ranges are "this host". A range inside the loopback network is already written as a
    /// constant by both templates; a range AROUND it (`local 0.0.0.0/0`, a host that answers
    /// everywhere) cannot sit beside the constant in an interval set and would make the mail
    /// table refuse every client, so it is left out of both and is out of this lockdown's
    /// reach.
    #[must_use]
    pub fn ranges_beside_loopback(&self) -> Vec<Ipv4Prefix> {
        let loopback = Ipv4Prefix::new(LOOPBACK_ADDRESS, LOOPBACK_LENGTH);
        let mut ranges: Vec<Ipv4Prefix> = self
            .local_prefixes
            .iter()
            .copied()
            .filter(|range| {
                !loopback.is_some_and(|loopback| loopback.covers(range) || range.covers(&loopback))
            })
            .collect();
        ranges.sort();
        ranges.dedup();

        ranges
    }

    /// The addresses the lockdown lists, in the order read: every address except the
    /// loopback ones and the unspecified one (never a destination), which both templates
    /// write as constants or never. The other half of the filter
    /// [`Self::ranges_beside_loopback`] is.
    pub fn addresses_beside_loopback(&self) -> impl Iterator<Item = IpAddr> + '_ {
        self.addresses
            .iter()
            .copied()
            .filter(|address| !address.is_loopback() && !address.is_unspecified())
    }

    /// The same facts sorted and without repeats, so two readings of one host
    /// compare equal whatever order the kernel listed them in.
    #[must_use]
    pub fn normalized(&self) -> Self {
        let mut addresses = self.addresses.clone();
        addresses.sort();
        addresses.dedup();
        let mut local_prefixes = self.local_prefixes.clone();
        local_prefixes.sort();
        local_prefixes.dedup();

        Self {
            addresses,
            local_prefixes,
        }
    }
}

#[cfg(test)]
#[path = "../tests/utils/host_addresses_tests.rs"]
mod tests;
