//! Reads this host's own IPv4 addresses out of the kernel's routing trie.

use std::net::Ipv4Addr;

use crate::utils::parse_fib_trie::parse_fib_trie;

/// The addresses `text` (the content of the kernel's `fib_trie` table) marks as
/// local to this host, each once, in the order first seen.
///
/// Only a `/32 LOCAL` route makes a leaf an address of this host, whatever its scope
/// (`/32 host LOCAL` from an interface address, `/32 universe LOCAL` from `ip route add
/// local 192.0.2.5/32 dev lo`); a `/8 host LOCAL` under `127.0.0.0` is the loopback
/// network, not an address. The lines
/// are read by [`parse_fib_trie`]; a table with none of them lists no addresses
/// and never panics, and the caller treats an EMPTY list from a readable table as
/// the fault it is, since every host has a loopback address.
#[must_use]
pub fn parse_fib_trie_local(text: &str) -> Vec<Ipv4Addr> {
    let mut found: Vec<Ipv4Addr> = Vec::new();

    for route in parse_fib_trie(text) {
        if route.is_local_address() && !found.contains(&route.leaf) {
            found.push(route.leaf);
        }
    }

    found
}

#[cfg(test)]
#[path = "../tests/utils/parse_fib_trie_local_tests.rs"]
mod tests;
