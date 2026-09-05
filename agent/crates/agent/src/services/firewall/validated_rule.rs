//! Turning an allow or a deny into the rule and the two host ports it needs.

use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::source_cidr::SourceCidr;
use maran_ops::firewall::{FirewallRule, NftablesProtocol, RulesetPorts};

use crate::proto::{AgentError, Protocol};
use crate::services::firewall::validated_ports::validated_ports;
use crate::services::sites::invalid_input::invalid_input;

/// The refusal for a request that names no transport protocol.
const MISSING_PROTOCOL: &str = "a firewall rule needs a protocol: tcp or udp";

/// Builds the rule and the host ports an `AllowPort` or a `DenyPort` carries.
///
/// One bundle for both because the two requests carry exactly the same five
/// values, and because getting either wrong has the same consequence: both
/// re-render the WHOLE ruleset, so a deny can lock an operator out as
/// thoroughly as an allow can.
///
/// The host ports are checked by [`validated_ports`], which both mutations and
/// the listing share — an empty `ssh_ports` and a zero port are refused there,
/// never defaulted, and that file carries why.
///
/// # Errors
///
/// Returns the wire error for an empty `ssh_ports`, for a rule port, an ssh
/// port or a panel port outside 1..=65535 (which includes the 0 an absent field
/// decodes to), for a protocol the contract does not name, and for a source
/// that is not a CIDR network.
pub fn validated_rule(
    port: u32,
    protocol: i32,
    source_cidr: &str,
    ssh_ports: &[u32],
    panel_port: u32,
) -> Result<(RulesetPorts, FirewallRule), AgentError> {
    let ports = validated_ports(ssh_ports, panel_port)?;

    let rule = FirewallRule {
        port: Port::parse(port).map_err(|error| invalid_input(error.to_string()))?,
        protocol: validated_protocol(protocol)?,
        source: SourceCidr::parse(source_cidr).map_err(|error| invalid_input(error.to_string()))?,
    };

    Ok((ports, rule))
}

/// Turns the wire's protocol number into the keyword the ruleset renders.
///
/// `PROTOCOL_UNSPECIFIED` and any number outside the contract are refused
/// rather than defaulted to TCP. A rule silently becoming TCP would open a port
/// the operator did not ask about while leaving the one they did ask about
/// closed — under a drop policy, two wrong outcomes from one unset field.
///
/// # Errors
///
/// Returns the wire error when the value is not one of the two protocols the
/// contract names.
fn validated_protocol(protocol: i32) -> Result<NftablesProtocol, AgentError> {
    match Protocol::try_from(protocol) {
        Ok(Protocol::Tcp) => Ok(NftablesProtocol::Tcp),
        Ok(Protocol::Udp) => Ok(NftablesProtocol::Udp),
        Ok(Protocol::Unspecified) | Err(_) => Err(invalid_input(MISSING_PROTOCOL.to_owned())),
    }
}

#[cfg(test)]
#[path = "../../tests/services/firewall/validated_rule_tests.rs"]
mod tests;
