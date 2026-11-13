//! An IPv4 network as an address and a prefix length.

use std::fmt;
use std::net::Ipv4Addr;

/// An IPv4 network: the first address of the range and how many leading bits name
/// it.
///
/// Made only by [`Self::new`], which clears the host bits, so two spellings of one
/// network are one value and a set built from them never lists a range twice.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct Ipv4Prefix {
    /// The first address of the range.
    network: Ipv4Addr,
    /// How many leading bits name the range, 0 to 32.
    length: u8,
}

impl Ipv4Prefix {
    /// The network `address` belongs to when `length` leading bits are kept, or
    /// `None` when `length` is over 32.
    #[must_use]
    pub fn new(address: Ipv4Addr, length: u8) -> Option<Self> {
        (length <= 32).then(|| Self {
            network: Ipv4Addr::from(u32::from(address) & Self::mask(length)),
            length,
        })
    }

    /// The first address of the range.
    #[must_use]
    pub fn network(&self) -> Ipv4Addr {
        self.network
    }

    /// How many leading bits name the range.
    #[must_use]
    pub fn length(&self) -> u8 {
        self.length
    }

    /// Whether `address` is inside the range.
    #[must_use]
    pub fn contains(&self, address: Ipv4Addr) -> bool {
        u32::from(address) & Self::mask(self.length) == u32::from(self.network)
    }

    /// Whether every address of `other` is inside this range (a range covers
    /// itself).
    #[must_use]
    pub fn covers(&self, other: &Self) -> bool {
        self.length <= other.length && self.contains(other.network)
    }

    /// The bit mask of `length` leading ones. A shift by 32 is not defined for a
    /// `u32`, so the empty prefix is spelled out.
    fn mask(length: u8) -> u32 {
        if length == 0 {
            0
        } else {
            u32::MAX << (32 - u32::from(length))
        }
    }
}

impl fmt::Display for Ipv4Prefix {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}/{}", self.network, self.length)
    }
}

#[cfg(test)]
#[path = "../tests/utils/ipv4_prefix_tests.rs"]
mod tests;
