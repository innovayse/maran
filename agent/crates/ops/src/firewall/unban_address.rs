//! UnbanAddress: take one address back out of the ban sets.

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::web::ban_address::BanAddress;
use maran_distro::DistroAdapter;

use crate::firewall::ban_address::{CLOSE_BRACE, ELEMENT, OPEN_BRACE, banned_set};
use crate::firewall::ensure_bans_table::{BANS_TABLE, TABLE_FAMILY};
use crate::firewall::firewall_error::FirewallError;
use crate::firewall::firewall_host::FirewallHost;
use crate::firewall::firewall_lock::firewall_lock;

/// `nft`'s verb for taking something out of a set.
const DELETE: &str = "delete";

/// Lifts the ban on `address`.
///
/// # Idempotency
///
/// An address with no ban in force is [`FirewallError::NotFound`], which is
/// the answer to a repeated unban. A host whose bans table is not loaded at
/// all gives the same answer, and correctly so: no table means no bans, so
/// there is nothing to lift.
///
/// The bans table is deliberately NOT ensured here. Loading it to discover
/// that it holds no bans would be work in the service of an answer already
/// known, and it would turn a read-shaped request into one that writes to
/// `/etc` on a host that never asked for a firewall.
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
/// - [`FirewallError::NotFound`] when no ban on that address is in force.
/// - [`FirewallError::NftFailed`] when `nft` cannot be started.
pub fn unban_address(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    address: &BanAddress,
) -> Result<(), FirewallError> {
    let _guard = firewall_lock();

    let deleted = delete_ban_element(host, distro, address)?;
    if deleted.status != 0 {
        return Err(FirewallError::NotFound);
    }

    Ok(())
}

/// Asks `nft` to delete `address` from the set for its family, and returns
/// what it made of that.
///
/// The outcome is returned rather than judged so that the one caller decides:
/// a non-zero status here means "there was no such ban", which is an answer
/// and not a failure, while an `nft` that cannot be run at all is a failure.
/// It is private to this file — `ban_address` used to call it too, before a
/// live kernel showed that `nft add element` refreshes an existing element by
/// itself and the delete it did first was only opening a window in which the
/// address was unbanned.
///
/// The element's text comes from [`BanAddress`]'s own `Display`, which writes
/// the one canonical spelling the type accepts — so the address `nft` is
/// asked to delete is spelled exactly as the address it was asked to add,
/// and a ban cannot be added under one spelling and left behind under
/// another.
///
/// # Errors
///
/// Returns [`FirewallError::NftFailed`] when `nft` cannot be started or
/// waited for.
fn delete_ban_element(
    host: &dyn FirewallHost,
    distro: &dyn DistroAdapter,
    address: &BanAddress,
) -> Result<CommandOutcome, FirewallError> {
    let element = address.to_string();

    host.run(
        distro.nft_binary(),
        &[
            DELETE,
            ELEMENT,
            TABLE_FAMILY,
            BANS_TABLE,
            banned_set(address),
            OPEN_BRACE,
            element.as_str(),
            CLOSE_BRACE,
        ],
    )
}

#[cfg(test)]
#[path = "../tests/firewall/unban_address_tests.rs"]
mod tests;
