//! One route line of the kernel's routing trie, under the leaf that names it.

use std::net::Ipv4Addr;

/// The type word of a route the host answers on.
const LOCAL_KIND: &str = "LOCAL";

/// One route of `/proc/net/fib_trie`: the leaf address it hangs under and the
/// `/<length> <scope> <kind>` the trie prints for it.
///
/// Made only by [`parse_fib_trie`](crate::utils::parse_fib_trie::parse_fib_trie),
/// which reads the table's line shape once; the callers that want the host's
/// addresses or its local ranges filter the list through the two questions below
/// instead of each reading the lines again.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FibRoute {
    /// The leaf address the route is listed under.
    pub leaf: Ipv4Addr,
    /// The prefix length the route covers, 0 to 255 as printed (a value over 32
    /// answers neither question).
    pub length: u8,
    /// The scope word (`host`, `link`, `universe`, ...). Printed for the reader's sake and
    /// deliberately NOT a test of either question below: the scope depends on how the route was
    /// made (`ip addr add` prints `host`, `ip route add local 192.0.2.5/32 dev lo` prints
    /// `universe`), while the host answers on both.
    pub scope: String,
    /// The type word (`LOCAL`, `UNICAST`, `BROADCAST`, ...).
    pub kind: String,
}

impl FibRoute {
    /// Whether the route makes [`Self::leaf`] an address this host answers on: a single-address
    /// route (`/32`) of the `LOCAL` type, whatever its scope (`/32 host LOCAL` from an interface
    /// address, `/32 universe LOCAL` from a `local` route added by hand).
    #[must_use]
    pub fn is_local_address(&self) -> bool {
        self.length == 32 && self.kind == LOCAL_KIND
    }

    /// Whether the route makes every address of a range wider than one address
    /// answer on this host: a `LOCAL` route shorter than `/32`, whatever its scope.
    #[must_use]
    pub fn is_local_range(&self) -> bool {
        self.length < 32 && self.kind == LOCAL_KIND
    }
}
