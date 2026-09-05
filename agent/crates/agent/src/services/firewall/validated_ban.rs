//! Turning a `BanAddress` request into the address and the life of the ban.

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// Builds the address and the ban's lifetime from what `BanAddress` carries.
///
/// `duration_seconds` is 0 for a ban with no expiry, which the contract states
/// and which is the one place in this service where a zero is a value rather
/// than an absent field. It becomes `None` — "no timeout" — rather than a zero
/// `Duration`, because `nft` reads `timeout 0s` as no timeout anyway and
/// writing the clause would say the same thing in a form that reads like a
/// mistake.
///
/// The request's `reason` is not read, here or anywhere: it is deprecated in
/// `firewall.proto` and the agent stores no reason. The only place one could go
/// on this side is an nftables `comment`, whose argument `nft` parses in its
/// own grammar — an injection primitive for a string the panel composes.
///
/// # Errors
///
/// Returns the wire error for anything that is not a single IPv4 or IPv6
/// address, and for a loopback address.
///
/// This calls [`BanAddress::parse`] directly rather than going through the
/// unban path's `validated_address`, and the difference is the point: the ban
/// direction refuses `127.0.0.0/8` and `::1`, because both nftables tables
/// accept `iif "lo"` before either ban set is consulted and such a ban would
/// be installed, reported as placed, and block nothing. The unban direction
/// must still accept those addresses so a leftover from an older version can
/// be lifted.
pub fn validated_ban(
    address: &str,
    duration_seconds: u32,
) -> Result<(BanAddress, Option<Duration>), AgentError> {
    let address = BanAddress::parse(address).map_err(|error| invalid_input(error.to_string()))?;
    let lifetime = (duration_seconds > 0).then(|| Duration::from_secs(u64::from(duration_seconds)));

    Ok((address, lifetime))
}

#[cfg(test)]
#[path = "../../tests/services/firewall/validated_ban_tests.rs"]
mod tests;
