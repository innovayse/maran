//! Reads the IPv4 ranges this host answers on out of the kernel's routing trie.

use crate::utils::ipv4_prefix::Ipv4Prefix;
use crate::utils::parse_fib_trie::parse_fib_trie;

/// The ranges `text` (the content of the kernel's `fib_trie` table) marks as
/// local to this host and wider than one address, each once, in the order first
/// seen.
///
/// A leaf line names the first address of a range and the lines below it name the
/// routes through it; a route such as `/24 universe LOCAL` (what `ip route add
/// local 192.0.2.0/24 dev lo` creates) makes EVERY address of the range answer on
/// this host. The single addresses (`/32 host LOCAL`) are
/// [`parse_fib_trie_local`](crate::utils::parse_fib_trie_local::parse_fib_trie_local)'s.
/// The loopback range (`/8 host LOCAL` under `127.0.0.0`) is listed here like any
/// other; the caller that writes it as a constant drops it. The lines are read by
/// [`parse_fib_trie`]; a malformed table lists fewer ranges and never panics.
#[must_use]
pub fn parse_fib_trie_local_prefixes(text: &str) -> Vec<Ipv4Prefix> {
    let mut found: Vec<Ipv4Prefix> = Vec::new();

    for route in parse_fib_trie(text) {
        if !route.is_local_range() {
            continue;
        }
        if let Some(prefix) = Ipv4Prefix::new(route.leaf, route.length)
            && !found.contains(&prefix)
        {
            found.push(prefix);
        }
    }

    found
}

#[cfg(test)]
#[path = "../tests/utils/parse_fib_trie_local_prefixes_tests.rs"]
mod tests;
