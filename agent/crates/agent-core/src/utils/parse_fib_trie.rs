//! Reads the kernel's routing trie into its routes.

use std::net::Ipv4Addr;

use crate::utils::fib_route::FibRoute;

/// The prefix of a line naming a leaf of the trie.
const LEAF_PREFIX: &str = "|-- ";

/// Every route `text` (the content of the kernel's `fib_trie` table) lists, in
/// the order printed.
///
/// A leaf line names an address and the lines below it name its routes as
/// `/<length> <scope> <kind>`. A line that is not in that shape, a route under a
/// leaf that is not an address and a route before any leaf are skipped, so a
/// malformed table lists fewer routes and never panics; the caller treats an EMPTY
/// answer from a readable table as the fault it is, since every host has a
/// loopback address.
#[must_use]
pub fn parse_fib_trie(text: &str) -> Vec<FibRoute> {
    let mut routes: Vec<FibRoute> = Vec::new();
    let mut leaf: Option<Ipv4Addr> = None;

    for line in text.lines() {
        let line = line.trim();
        if let Some(rest) = line.strip_prefix(LEAF_PREFIX) {
            leaf = rest.trim().parse().ok();
            continue;
        }
        let Some(leaf) = leaf else {
            continue;
        };
        let mut words = line.split_whitespace();
        let (Some(length), Some(scope), Some(kind), None) =
            (words.next(), words.next(), words.next(), words.next())
        else {
            continue;
        };
        let Some(length) = length
            .strip_prefix('/')
            .and_then(|digits| digits.parse::<u8>().ok())
        else {
            continue;
        };
        routes.push(FibRoute {
            leaf,
            length,
            scope: scope.to_owned(),
            kind: kind.to_owned(),
        });
    }

    routes
}

#[cfg(test)]
#[path = "../tests/utils/parse_fib_trie_tests.rs"]
mod tests;
