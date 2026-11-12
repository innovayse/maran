//! Reads the addresses configured on this host's interfaces from the kernel.

use std::fs::File;
use std::io::{ErrorKind, Read};

use crate::utils::host_addresses::HostAddresses;
use crate::utils::host_addresses_error::HostAddressesError;
use crate::utils::parse_host_addresses::{
    ADDRESS_TABLE_LIMIT, PROC_FIB_TRIE, PROC_IF_INET6, parse_host_addresses,
};

/// How reading one kernel address table failed.
enum TableFault {
    /// The file does not exist.
    Missing,
    /// The file is longer than [`ADDRESS_TABLE_LIMIT`].
    OverBound,
    /// The file exists and the read failed, or its content is not text.
    Unreadable,
}

/// Reads one kernel address table, bounded.
///
/// # Errors
///
/// Returns the [`TableFault`] that names why the table could not be read.
fn read_address_table(path: &str) -> Result<String, TableFault> {
    let file = File::open(path).map_err(|error| match error.kind() {
        ErrorKind::NotFound => TableFault::Missing,
        _ => TableFault::Unreadable,
    })?;
    let mut text = String::new();
    file.take(u64::try_from(ADDRESS_TABLE_LIMIT).map_or(u64::MAX, |limit| limit.saturating_add(1)))
        .read_to_string(&mut text)
        .map_err(|_| TableFault::Unreadable)?;

    if text.len() > ADDRESS_TABLE_LIMIT {
        return Err(TableFault::OverBound);
    }

    Ok(text)
}

/// Every IPv4 and IPv6 address configured on an interface of this host, and the
/// IPv4 ranges it answers on, read from the kernel's own tables, IPv4 first.
///
/// Also what the agent's periodic check of the host's addresses reads, so the
/// startup reconcile and every later refresh of both port-25 layers see the same
/// host.
///
/// Asked by two areas that must agree on what "this host" means: the migration
/// source policy (a source on the host itself is refused) and the mail stack (a
/// customer's connection from one of the host's own addresses is not a delivery
/// from outside), so the reading lives here and not in either.
///
/// # Errors
///
/// Returns [`HostAddressesError::Ipv4Missing`], [`HostAddressesError::Ipv4Unreadable`]
/// or [`HostAddressesError::Ipv4OverBound`] by how the IPv4 table failed, and
/// [`HostAddressesError::Ipv6Unreadable`] or [`HostAddressesError::Ipv6OverBound`]
/// when the IPv6 table exists and failed. An absent IPv6 table is IPv6 turned off,
/// which lists nothing.
pub fn read_host_addresses() -> Result<HostAddresses, HostAddressesError> {
    let fib_trie = match read_address_table(PROC_FIB_TRIE) {
        Ok(text) => Some(text),
        Err(TableFault::Missing) => None,
        Err(TableFault::OverBound) => return Err(HostAddressesError::Ipv4OverBound),
        Err(TableFault::Unreadable) => return Err(HostAddressesError::Ipv4Unreadable),
    };
    let if_inet6 = match read_address_table(PROC_IF_INET6) {
        Ok(text) => Some(text),
        Err(TableFault::Missing) => None,
        Err(TableFault::OverBound) => return Err(HostAddressesError::Ipv6OverBound),
        Err(TableFault::Unreadable) => return Err(HostAddressesError::Ipv6Unreadable),
    };

    parse_host_addresses(fib_trie.as_deref(), if_inet6.as_deref())
}

#[cfg(test)]
#[path = "../tests/utils/read_host_addresses_tests.rs"]
mod tests;
