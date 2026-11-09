//! Revalidating the zone name an rpc carries.

use maran_agent_core::validation::dns::zone_name::ZoneName;

use crate::proto::AgentError;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the zone apex an rpc carries.
///
/// The panel validated it already. This is the agent's own check, and it decides
/// the name of a file the root daemon reads, a command-line argument of `rndc`
/// and of the zone checker, and a line of the zone file's `$ORIGIN`, which is why
/// the gate is shared by every zone rpc and unconditional.
///
/// # Errors
///
/// Returns the wire error for a name the agent will not accept.
pub fn validated_zone_name(zone_name: &str) -> Result<ZoneName, AgentError> {
    ZoneName::parse(zone_name).map_err(|error| invalid_input(format!("zone name: {error}")))
}

#[cfg(test)]
#[path = "../../tests/services/dns/validated_zone_name_tests.rs"]
mod tests;
