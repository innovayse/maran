//! The one question two validators ask about an IPv6 address: is it really an
//! IPv4 one?
//!
//! Subject-named, like `validation/fs/path.rs`: the file is named after the
//! question rather than after the function that answers it, because the answer
//! is the unit and the folder already carries the noun. It holds one
//! crate-visible predicate and no error type — the answer is an `Option`, and
//! each caller reports the refusal in its own error enum, in its own words.

use std::net::{Ipv4Addr, Ipv6Addr};

/// The IPv4 address `v6` is really naming, if it is naming one.
///
/// Answers `Some` for the two notations that put an IPv4 host inside an IPv6
/// address: the v4-mapped form `::ffff:1.2.3.4`, and the v4-compatible form
/// `::1.2.3.4` that RFC 4291 §2.5.5.1 deprecated (and that `std` renders back
/// as `::102:304`). `::` and `::1` are the two exceptions, and they are checked
/// first — see below.
///
/// This lives in a file of its own rather than as a private helper in each of
/// its two callers because it was written twice once already, and the pair did
/// not survive review: a mutation that broke one copy's first branch left the
/// suite green, which is exactly the kind of subtlety that diverges when only
/// one copy gets the next fix.
///
/// # Why there is no `to_ipv4_mapped` branch
///
/// The obvious implementation asks `Ipv6Addr::to_ipv4_mapped` first and falls
/// back to `Ipv6Addr::to_ipv4`. That first branch cannot ever be the deciding
/// one: `to_ipv4` answers `Some` for a mapped address too, so deleting the
/// branch changes no output for any input, and no test can tell the two
/// versions apart. It was written that way here and the mutation pass proved
/// it — breaking the mapped branch alone SURVIVED. A defensive call that cannot
/// fail is deleted rather than labelled (rules/testing.md), so it is deleted:
/// what remains is one call that decides and one guard that carves out its two
/// exceptions, and each of the three conditions below is independently
/// mutation-killable.
///
/// # Why the exceptions come first
///
/// `Ipv6Addr::to_ipv4` answers `Some(0.0.0.0)` for `::` and `Some(0.0.0.1)` for
/// `::1`, because both have the ninety-six leading zero bits the v4-compatible
/// form is defined by. Those are ordinary IPv6 addresses that every host uses —
/// the unspecified address and loopback — so they are refused from the refusal,
/// not from the crate. Verified against the standard library rather than
/// assumed, and it is the trap the obvious implementation falls into.
///
/// # Why the answer matters
///
/// The firewall keeps one rule path and one ban set per address family, and
/// both callers ask their own `is_v4()` which one to use. An IPv4 host arriving
/// as an IPv6 address would be rendered into the IPv6 side and would then match
/// nothing an IPv4 packet carries: a source restriction that restricts nothing,
/// or a ban that bans nothing.
pub(crate) fn ipv4_in_disguise(v6: Ipv6Addr) -> Option<Ipv4Addr> {
    if v6.is_unspecified() || v6.is_loopback() {
        return None;
    }

    v6.to_ipv4()
}

#[cfg(test)]
#[path = "../../tests/validation/web/ipv4_disguise_tests.rs"]
mod tests;
