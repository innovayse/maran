//! The address policy: which address of a source the agent may connect to.

use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

use crate::transfer::model::source_endpoint::SourceEndpoint;
use crate::transfer::transfer_host::TransferHost;
use crate::transfer::transfer_op_error::TransferOpError;

/// Resolves the source's host once and returns the one address to connect to.
///
/// **The policy is applied to the RESOLVED address, not to the name.** A name is
/// whatever its DNS operator says, so a host name that looks public can answer
/// `127.0.0.1`, an address of this host, or an address that embeds one. The
/// connection is then made to this returned literal and never to the name, with
/// the host key looked up under a fixed alias, so the answer checked here is the
/// answer connected to: there is no second resolution for a rebinding name to
/// answer differently.
///
/// An address is refused when it is loopback, unspecified (including the whole
/// `0.0.0.0/8` block), multicast, broadcast, IPv4 or IPv6 link-local, or one of
/// this host's own addresses, and an IPv6 address that EMBEDS an IPv4 one
/// (`::ffff:a.b.c.d`, the deprecated `::a.b.c.d`, the NAT64 `64:ff9b::/96`) is
/// judged by the embedded address as well. Private ranges are allowed on
/// purpose (rules for this surface: a source inside the operator's own network
/// is the ordinary case).
///
/// **Every** address the name resolves to must pass: a name that points even one
/// record at a forbidden address is refused whole, so a rotating answer cannot
/// hand a forbidden address to a later call. The first resolved address is the
/// one used.
///
/// # Errors
///
/// Returns [`TransferOpError::AddressRefused`] when any resolved address is
/// refused; [`TransferOpError::Unreachable`] when the name does not resolve;
/// [`TransferOpError::HostAddressesUnavailable`] when this host's own addresses
/// cannot be listed (the policy fails closed).
pub fn choose_source_address(
    host: &dyn TransferHost,
    endpoint: &SourceEndpoint,
) -> Result<IpAddr, TransferOpError> {
    let candidates = match endpoint.host.literal_address() {
        Some(literal) => vec![literal],
        None => host.resolve(&endpoint.host, endpoint.port)?,
    };

    let own = host.local_addresses()?;

    if candidates.iter().any(|address| is_refused(address, &own)) {
        return Err(TransferOpError::AddressRefused);
    }

    candidates
        .first()
        .copied()
        .ok_or(TransferOpError::Unreachable)
}

/// The IPv4 address an IPv6 address stands for, if it embeds one.
fn embedded_ipv4(address: &Ipv6Addr) -> Option<Ipv4Addr> {
    if let Some(mapped) = address.to_ipv4_mapped() {
        return Some(mapped);
    }

    let segments = address.segments();
    let low = Ipv4Addr::new(
        (segments[6] >> 8) as u8,
        (segments[6] & 0xff) as u8,
        (segments[7] >> 8) as u8,
        (segments[7] & 0xff) as u8,
    );
    // `::0.0.0.1` is `::1`, the loopback, and `::` is the unspecified address:
    // neither stands for an IPv4 host, and both are refused as themselves.
    let leading_zero = segments[..6].iter().all(|part| *part == 0);
    let nat64 = segments[..2] == [0x0064, 0xff9b] && segments[2..6].iter().all(|part| *part == 0);

    if (leading_zero && !low.is_unspecified() && low != Ipv4Addr::new(0, 0, 0, 1)) || nat64 {
        return Some(low);
    }

    None
}

/// Whether the agent never connects to `address`.
fn is_refused(address: &IpAddr, own: &[IpAddr]) -> bool {
    let canonical = address.to_canonical();

    if own.iter().any(|mine| mine.to_canonical() == canonical) {
        return true;
    }

    match canonical {
        IpAddr::V4(v4) => is_refused_ipv4(&v4),
        IpAddr::V6(v6) => {
            v6.is_loopback()
                || v6.is_unspecified()
                || v6.is_multicast()
                || v6.is_unicast_link_local()
                || embedded_ipv4(&v6).is_some_and(|inner| {
                    is_refused_ipv4(&inner) || own.contains(&IpAddr::V4(inner))
                })
        }
    }
}

/// Whether an IPv4 address is one the agent never connects to.
fn is_refused_ipv4(address: &Ipv4Addr) -> bool {
    address.is_loopback()
        || address.is_unspecified()
        || address.octets()[0] == 0
        || address.is_multicast()
        || address.is_broadcast()
        || address.is_link_local()
}

#[cfg(test)]
#[path = "../tests/transfer/choose_source_address_tests.rs"]
mod tests;
