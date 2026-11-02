//! Turning a `BanAddress` request into the address and the life of the ban.

use std::time::Duration;

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::proto::{AgentError, BanAddressRequest};
use crate::services::firewall::ban_lifetime::ban_lifetime;
use crate::services::wire::invalid_input::invalid_input;

/// Builds the address and the ban's lifetime from what `BanAddress` carries.
///
/// The ban's life is read by [`ban_lifetime`]: the request's `term` when it
/// carries one, the deprecated `duration_seconds` (0 for a ban with no expiry)
/// only for a request from a panel older than revision 13. It is `None` for a
/// ban with no timeout, never a zero `Duration`.
///
/// The request's `reason` is not read, here or anywhere: it is deprecated in
/// `firewall.proto` and the agent stores no reason. The only place one could go
/// on this side is an nftables `comment`, whose argument `nft` parses in its
/// own grammar — an injection primitive for a string the panel composes.
///
/// # Errors
///
/// Returns the wire error for anything that is not a single IPv4 or IPv6
/// address, for a loopback address, and for a timed term of zero seconds.
///
/// This calls [`BanAddress::parse`] directly rather than going through the
/// unban path's `validated_address`, and the difference is the point: the ban
/// direction refuses `127.0.0.0/8` and `::1`, because both nftables tables
/// accept `iif "lo"` before either ban set is consulted and such a ban would
/// be installed, reported as placed, and block nothing. The unban direction
/// must still accept those addresses so a leftover from an older version can
/// be lifted.
pub fn validated_ban(
    request: &BanAddressRequest,
) -> Result<(BanAddress, Option<Duration>), AgentError> {
    let address =
        BanAddress::parse(&request.address).map_err(|error| invalid_input(error.to_string()))?;
    let lifetime = ban_lifetime(request.term.as_ref(), request.duration_seconds)?;

    Ok((address, lifetime))
}

#[cfg(test)]
#[path = "../../tests/services/firewall/validated_ban_tests.rs"]
mod tests;
