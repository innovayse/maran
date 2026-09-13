//! Revalidating the address an unban names.

use maran_agent_core::validation::web::ban_address::BanAddress;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the address `UnbanAddress` carries.
///
/// The address becomes an argument of an `nft` invocation that runs as root, so
/// it is a validated type and never a `String`: [`BanAddress`] holds a parsed
/// `IpAddr` and writes its own text back, which means no spelling of an address
/// reaches that argument vector that this agent did not itself produce. It also
/// decides WHICH set the element is deleted from, since the two sets are typed
/// by address family.
///
/// # Errors
///
/// Returns the wire error for anything that is not a single IPv4 or IPv6
/// address — a network with a prefix, a hostname, or an address with anything
/// around it.
///
/// # Why the unban path accepts an address the ban path refuses
///
/// This calls [`BanAddress::parse_existing`], which checks the same form as
/// [`BanAddress::parse`] but does not refuse loopback. Refusing a loopback ban
/// is a protection; refusing to LIFT one is the opposite, and a host upgraded
/// from a version without the ban-side refusal can still hold a loopback
/// element that an administrator must be able to remove.
pub fn validated_address(address: &str) -> Result<BanAddress, AgentError> {
    BanAddress::parse_existing(address).map_err(|error| invalid_input(error.to_string()))
}

#[cfg(test)]
#[path = "../../tests/services/firewall/validated_address_tests.rs"]
mod tests;
