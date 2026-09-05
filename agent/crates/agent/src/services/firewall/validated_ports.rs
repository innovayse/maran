//! Revalidating the host ports every firewall rpc carries.

use maran_agent_core::validation::web::port::Port;
use maran_ops::firewall::RulesetPorts;

use crate::proto::AgentError;
use crate::services::sites::invalid_input::invalid_input;

/// The refusal for a request that names no SSH port at all.
const NO_SSH_PORTS: &str =
    "a firewall request must name every port this host's sshd listens on; the list was empty";

/// Builds the host ports every firewall rpc carries — the mutations and the
/// listing alike.
///
/// **The SSH ports are a LIST, and an empty one is REFUSED rather than
/// defaulted.** sshd listens on every `Port` directive and on every
/// `ListenAddress host:port`, across the main config and everything its
/// `Include` pulls in — which on the Debian family is where a port override
/// usually lives. One port would open that one and close the rest, and which
/// one it happened to be would depend on line order in a config file.
///
/// A proto3 `repeated` field a caller did not set arrives EMPTY and a `uint32`
/// it did not set arrives as 0, which [`Port::parse`] rejects — so a panel that
/// does not send these gets a refusal and nothing changes. Defaulting to `[22]`
/// would be worse than useless: the installer already falls back to 22 and logs
/// it when its own detection finds nothing, so an empty list reaching the agent
/// means something upstream broke, and a guess on top of a broken upstream
/// renders a `policy drop` ruleset with an accept for a port nothing listens on
/// and none for the port the operator is connected through.
///
/// One bad element refuses the whole request rather than being dropped from the
/// list. A dropped port is a port the rendered policy closes, which is the
/// failure this list exists to prevent.
///
/// [`RulesetPorts`] is built here rather than passed as loose arguments for the
/// reason that type exists: values of one type side by side can be swapped
/// without the compiler saying a word, and the swap renders SSH's hard allow
/// for the panel's port and the panel's for SSH's.
///
/// # Errors
///
/// Returns the wire error for an empty `ssh_ports`, and for any ssh port or the
/// panel port outside 1..=65535 — which includes the 0 an absent field decodes
/// to.
pub fn validated_ports(ssh_ports: &[u32], panel_port: u32) -> Result<RulesetPorts, AgentError> {
    if ssh_ports.is_empty() {
        return Err(invalid_input(NO_SSH_PORTS.to_owned()));
    }

    let ssh_ports = ssh_ports
        .iter()
        .map(|candidate| Port::parse(*candidate).map_err(|error| invalid_input(error.to_string())))
        .collect::<Result<Vec<_>, _>>()?;

    Ok(RulesetPorts {
        ssh_ports,
        panel_port: Port::parse(panel_port).map_err(|error| invalid_input(error.to_string()))?,
    })
}

#[cfg(test)]
#[path = "../../tests/services/firewall/validated_ports_tests.rs"]
mod tests;
