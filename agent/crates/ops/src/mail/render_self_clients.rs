//! Rendering the table of this host's own client addresses.

use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use maran_agent_core::utils::host_addresses::HostAddresses;
use maran_agent_core::utils::ipv4_prefix::Ipv4Prefix;
use maran_templates::postfix::postfix_self_clients::PostfixSelfClients;

use crate::mail::mail_error::MailError;

/// Renders the CIDR table the port 25 service refuses, from the addresses the
/// host's interfaces carry and the ranges its routing table marks local.
///
/// The loopback ranges are written by the template as whole networks, so a
/// loopback or unspecified address, and a local range inside or around the
/// loopback range (the filter the firewall's address sets use too:
/// [`HostAddresses::ranges_beside_loopback`]), is dropped here rather than listed a
/// second time. A range is listed whole (a
/// routed block answers on this host at every address), and an address inside a
/// listed range is dropped, since the range already refuses it. The rest are
/// sorted and made unique,
/// so the bytes depend on WHICH addresses the host has and not on the order the
/// kernel listed them in: a table that re-ordered between two reads would look
/// changed to the convergence check and bounce Postfix for nothing.
///
/// # Errors
///
/// Returns [`MailError::Render`] when the template and its render type have
/// drifted apart.
pub(crate) fn render_self_clients(host: &HostAddresses) -> Result<String, MailError> {
    let mut ipv4: Vec<Ipv4Addr> = Vec::new();
    let mut ipv6: Vec<Ipv6Addr> = Vec::new();
    // The loopback filter is the one the firewall's address sets use too, so the
    // two layers cannot disagree about which ranges are "this host".
    let ranges: Vec<Ipv4Prefix> = host.ranges_beside_loopback();
    for address in host.addresses_beside_loopback() {
        match address {
            IpAddr::V4(v4) => {
                if !ranges.iter().any(|range| range.contains(v4)) {
                    ipv4.push(v4);
                }
            }
            IpAddr::V6(v6) => ipv6.push(v6),
        }
    }
    ipv4.sort();
    ipv4.dedup();
    ipv6.sort();
    ipv6.dedup();

    PostfixSelfClients {
        ipv4_ranges: ranges
            .iter()
            .map(|range| (range.network(), range.length()))
            .collect(),
        ipv4,
        ipv6,
    }
    .render_config()
    .map_err(MailError::from)
}

#[cfg(test)]
#[path = "../tests/mail/render_self_clients_tests.rs"]
mod tests;
