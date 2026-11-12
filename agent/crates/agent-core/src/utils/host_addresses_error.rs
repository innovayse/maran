//! Why the host's own addresses could not be read.

/// The kernel's address tables could not be read.
///
/// One variant per way it fails, so an operator reading the log can tell a table
/// that is not there from one that cannot be opened and from one that is too big.
#[derive(Debug, Clone, Copy, thiserror::Error, PartialEq, Eq)]
#[non_exhaustive]
pub enum HostAddressesError {
    /// The IPv4 table is absent. Every host has a loopback address, so an absent
    /// table is a fault and not an empty host.
    #[error("the kernel's IPv4 address table is missing")]
    Ipv4Missing,
    /// The IPv4 table exists and the read failed.
    #[error("the kernel's IPv4 address table cannot be read")]
    Ipv4Unreadable,
    /// The IPv4 table is over [`ADDRESS_TABLE_LIMIT`](crate::utils::parse_host_addresses::ADDRESS_TABLE_LIMIT).
    #[error("the kernel's IPv4 address table is over its bound")]
    Ipv4OverBound,
    /// The IPv6 table exists and the read failed. An absent table is IPv6 turned
    /// off and is not this error.
    #[error("the kernel's IPv6 address table cannot be read")]
    Ipv6Unreadable,
    /// The IPv6 table is over [`ADDRESS_TABLE_LIMIT`](crate::utils::parse_host_addresses::ADDRESS_TABLE_LIMIT).
    #[error("the kernel's IPv6 address table is over its bound")]
    Ipv6OverBound,
}
