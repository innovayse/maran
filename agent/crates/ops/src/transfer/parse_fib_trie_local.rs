//! Reads this host's own IPv4 addresses out of the kernel's routing trie.

use std::net::Ipv4Addr;

/// The marker the trie prints under an address that is configured on an
/// interface of this host: a host route (`/32`) of the `LOCAL` type.
const LOCAL_HOST_ROUTE: &str = "/32 host LOCAL";

/// The prefix of a line naming a leaf of the trie.
const LEAF_PREFIX: &str = "|-- ";

/// The addresses `text` (the content of the kernel's `fib_trie` table) marks as
/// local to this host, each once, in the order first seen.
///
/// A leaf line names an address and the lines below it name its routes; only a
/// `/32 host LOCAL` route makes the leaf an address of this host (a `/8 host
/// LOCAL` under `127.0.0.0` is the loopback network, not an address). A line
/// that is not in that shape is skipped, so a malformed table lists fewer
/// addresses and never panics; the caller treats an EMPTY list from a readable
/// table as the fault it is, since every host has a loopback address.
#[must_use]
pub fn parse_fib_trie_local(text: &str) -> Vec<Ipv4Addr> {
    let mut found: Vec<Ipv4Addr> = Vec::new();
    let mut leaf: Option<Ipv4Addr> = None;

    for line in text.lines() {
        let line = line.trim();
        if let Some(rest) = line.strip_prefix(LEAF_PREFIX) {
            leaf = rest.trim().parse().ok();
        } else if line.starts_with(LOCAL_HOST_ROUTE)
            && let Some(address) = leaf
            && !found.contains(&address)
        {
            found.push(address);
        }
    }

    found
}

#[cfg(test)]
#[path = "../tests/transfer/parse_fib_trie_local_tests.rs"]
mod tests;
