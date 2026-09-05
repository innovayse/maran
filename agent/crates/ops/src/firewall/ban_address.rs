//! BanAddress: drop everything from one address, for a while or until told.

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;
use maran_distro::DistroAdapter;

use crate::firewall::ensure_bans_table::{
    BANNED_V4_SET, BANNED_V6_SET, BANS_TABLE, TABLE_FAMILY, ensure_bans_table_under_lock,
};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;

/// `nft`'s verb for putting something into a set.
const ADD: &str = "add";

/// `nft`'s noun for a member of a set.
pub(super) const ELEMENT: &str = "element";

/// The brace `nft` expects around a set element list.
///
/// A separate argument rather than part of the element's text: `nft` joins
/// its argument vector with spaces and lexes the result in its own grammar,
/// so the braces are tokens like any other. Passing each token as its own
/// argv item was verified working against real nftables during review (rc=0
/// on the AlmaLinux 9 polygon), and it is what keeps the whole command a
/// vector this agent assembled rather than a string it formatted.
pub(super) const OPEN_BRACE: &str = "{";

/// The closing brace of a set element list. See [`OPEN_BRACE`].
pub(super) const CLOSE_BRACE: &str = "}";

/// `nft`'s keyword introducing an element's remaining lifetime.
const TIMEOUT: &str = "timeout";

/// Drops every packet from `address` until `ttl` runs out, or until somebody
/// unbans it when `ttl` is `None`.
///
/// # What the kernel is asked to do, in order
///
/// 1. The bans table is ensured — see
///    [`ensure_bans_table`](super::ensure_bans_table::ensure_bans_table),
///    which does nothing at all when the table is already loaded, because
///    re-applying its file would erase every ban in force.
/// 2. The element is added, with `timeout <n>s` when the ban has one.
///
/// **One `add`, and deliberately no delete before it.** `nft add element` on
/// an address the set already holds REPLACES that element and refreshes its
/// timeout, so a repeated ban extends by itself — which is what an escalating
/// brute-force policy needs. Measured on real nftables v1.0.9: 900s → 2h
/// extends, 2h → 1m shortens, permanent → timed and timed → permanent both
/// convert, and every one of those exits 0.
///
/// An earlier version of this operation deleted the element first, on the
/// belief that `add` would not refresh a timeout. That belief is false on the
/// version this project measures everything against, and the delete it
/// justified was not free: between two `nft` spawns the address was **not
/// banned**. The module lock serialises this agent's own callers; it does not
/// stop packets. So the delete bought nothing and opened a window, and the
/// operation now does the one thing the tool already does correctly.
///
/// # This agent stores no reason
///
/// The panel records who was banned, why and until when; the agent records
/// nothing (R6). An earlier design carried the operator's reason down here
/// and put it in an `nft` comment, which was an injection primitive — `nft`
/// parses its arguments in its own grammar — so the reason is panel metadata
/// only and there is no parameter for it here.
///
/// Both families' nftables units flush the ruleset when the service stops or
/// reloads, so a ban does not survive a restart or a reboot. That is by
/// design: the panel holds the durable record and re-applies the unexpired
/// bans on startup.
///
/// # A loopback address never reaches here
///
/// This operation takes a [`BanAddress`], and that type's `parse` refuses
/// `127.0.0.0/8` and `::1`. It has to: both tables this area renders accept
/// `iif "lo"` ahead of the ban sets, so an element for a loopback address
/// would be added, reported as placed, and match no packet — which is how an
/// audit journal comes to say an address was blocked when it was not. The
/// refusal is at the parse rather than here because that is the last gate
/// every caller passes, panel and operator alike.
///
/// # Calling this
///
/// Synchronous, and it MUST be invoked from `tokio::task::spawn_blocking` —
/// never awaited on a runtime worker. It spawns `nft`, writes to disk, and
/// takes the module lock with `blocking_lock`, which PANICS rather than block
/// a worker. See this area's module documentation for the whole requirement.
///
/// # Errors
///
/// - [`FirewallError::NftFailed`] when `nft` cannot be started, or refuses to
///   add the element.
/// - [`FirewallError::RenderFailed`], [`FirewallError::RuleRefusedByNft`],
///   [`FirewallError::StagingFailed`] when the bans table had to be loaded
///   and could not be — see
///   [`ensure_bans_table`](super::ensure_bans_table::ensure_bans_table).
pub fn ban_address(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    address: &BanAddress,
    ttl: Option<Duration>,
) -> Result<(), FirewallError> {
    // One guard for the whole operation, not one per step: the table check
    // and the add are a single change to a single host-wide resource, and a
    // lock released between them is a lock that closes nothing — the check
    // could then find the table absent, another caller could load the bans
    // file, and this caller's apply would erase the ban that one just added.
    let _guard = firewall_lock();

    ensure_bans_table_under_lock(host, distro)?;

    let set = banned_set(address);
    let element = address.to_string();
    // A zero duration is treated as "no timeout", because that is what `nft`
    // itself makes of `timeout 0s` — writing the clause would say the same
    // thing in a form that reads like a mistake.
    let lifetime = ttl
        .filter(|duration| !duration.is_zero())
        .map(|duration| format!("{}s", duration.as_secs()));

    let mut arguments = vec![
        ADD,
        ELEMENT,
        TABLE_FAMILY,
        BANS_TABLE,
        set,
        OPEN_BRACE,
        element.as_str(),
    ];
    if let Some(seconds) = lifetime.as_deref() {
        arguments.push(TIMEOUT);
        arguments.push(seconds);
    }
    arguments.push(CLOSE_BRACE);

    let added = host.run(distro.nft_binary(), &arguments)?;
    if added.status != 0 {
        return Err(FirewallError::NftFailed {
            stderr: added.stderr,
        });
    }

    Ok(())
}

/// The set an address belongs in.
///
/// The firewall keeps one set per address family, and the two are typed
/// (`ipv4_addr`, `ipv6_addr`), so an address put in the wrong one matches no
/// packet at all — a ban that silently bans nothing. [`BanAddress`] refuses
/// `::ffff:a.b.c.d` for exactly this reason: a v4-mapped address in the IPv6
/// set would never match the IPv4 packets it was ordered against.
pub(super) fn banned_set(address: &BanAddress) -> &'static str {
    if address.is_v4() {
        BANNED_V4_SET
    } else {
        BANNED_V6_SET
    }
}

#[cfg(test)]
#[path = "../tests/firewall/ban_address_tests.rs"]
mod tests;
