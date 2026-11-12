//! Turns the kernel's two address tables into this host's own addresses.

use std::net::IpAddr;

use crate::utils::host_addresses::HostAddresses;
use crate::utils::host_addresses_error::HostAddressesError;
use crate::utils::parse_fib_trie_local::parse_fib_trie_local;
use crate::utils::parse_fib_trie_local_prefixes::parse_fib_trie_local_prefixes;
use crate::utils::parse_if_inet6::parse_if_inet6;

/// The kernel's table of the host's IPv4 routes and local addresses.
///
/// A kernel interface, identical on every family and not a `distro` fact. The ONE
/// spelling of the path, for the reader of the real file
/// ([`read_host_addresses`](crate::utils::read_host_addresses::read_host_addresses))
/// and for the reader that goes through a host seam.
pub const PROC_FIB_TRIE: &str = "/proc/net/fib_trie";

/// The kernel's table of the host's IPv6 addresses. Absent when IPv6 is off.
pub const PROC_IF_INET6: &str = "/proc/net/if_inet6";

/// The most of either table that is read. A host carrying a full routing table
/// is over it and is refused rather than read whole.
pub const ADDRESS_TABLE_LIMIT: usize = 16 * 1024 * 1024;

/// The addresses configured on this host's interfaces, IPv4 first, and the IPv4
/// ranges its routing table marks local, from the text of `/proc/net/fib_trie` and
/// `/proc/net/if_inet6`.
///
/// The ONE reading of the two tables, with the ONE failure behaviour, for every
/// reader of them (the mail stage's refused-client table, the firewall's output
/// chain, the migration source policy): they must agree on what "this host" means
/// and on what to do when it cannot be known, and two readers with two failure
/// behaviours would let one layer render with no addresses while the other
/// refuses. A table the caller could not read is `None`.
///
/// # Errors
///
/// - [`HostAddressesError::Ipv4Missing`] when the IPv4 table is `None`. Every host
///   has a loopback address, so a missing table is a fault and not an empty
///   host, and the caller refuses to act on it.
/// - [`HostAddressesError::Ipv4OverBound`] when the IPv4 table is over
///   [`ADDRESS_TABLE_LIMIT`].
/// - [`HostAddressesError::Ipv6OverBound`] when the IPv6 table is over its bound.
///   An absent IPv6 table (`None`) is IPv6 turned off and lists nothing.
pub fn parse_host_addresses(
    fib_trie: Option<&str>,
    if_inet6: Option<&str>,
) -> Result<HostAddresses, HostAddressesError> {
    let fib_trie = fib_trie.ok_or(HostAddressesError::Ipv4Missing)?;
    if fib_trie.len() > ADDRESS_TABLE_LIMIT {
        return Err(HostAddressesError::Ipv4OverBound);
    }
    let mut addresses: Vec<IpAddr> = parse_fib_trie_local(fib_trie)
        .into_iter()
        .map(IpAddr::V4)
        .collect();

    if let Some(text) = if_inet6 {
        if text.len() > ADDRESS_TABLE_LIMIT {
            return Err(HostAddressesError::Ipv6OverBound);
        }
        addresses.extend(parse_if_inet6(text).into_iter().map(IpAddr::V6));
    }

    Ok(HostAddresses {
        addresses,
        local_prefixes: parse_fib_trie_local_prefixes(fib_trie),
    })
}

#[cfg(test)]
#[path = "../tests/utils/parse_host_addresses_tests.rs"]
mod tests;
