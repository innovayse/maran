//! Turning an `EnableFtps` request into the configuration the operation takes.

use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::passive_address::PassiveAddress;
use maran_agent_core::validation::web::port::Port;
use maran_ops::ftps::FtpsConfiguration;

use crate::proto::{AgentError, EnableFtpsRequest};
use crate::services::wire::invalid_input::invalid_input;

/// Builds the daemon configuration from the five values `EnableFtps` carries.
///
/// Every field of the result is a validated type rather than a `String` or a
/// bare integer, which is what keeps a newline out of a line-oriented
/// configuration file (rules/security.md item 4): the hostname and the passive
/// address both reach the rendered `vsftpd.conf`, and neither can hold one.
///
/// **The ordering check is here and not in the template.** A range whose lower
/// bound is above its upper bound renders a `vsftpd.conf` that parses and then
/// serves no passive connection at all — a daemon that greets, authenticates and
/// hangs on the first listing, which is the worst shape a failure can take
/// because every liveness check the enable path runs answers yes. It is refused
/// as input instead. The check is the agent's own and not a repetition of the
/// panel's: the panel decides these numbers from its defaults, and the agent
/// must not act on a pair it cannot render sanely whatever the panel believed
/// it sent (rules/security.md item 1).
///
/// **The listening mode is not here, and cannot be sent.** It is probed from the
/// kernel by the operation, never accepted from a caller. Neither are the
/// certificate paths: they are derived from `hostname` inside the agent's own
/// store, so no request can point the daemon at key material of its choosing.
///
/// An empty `passive_address` is `None` and not an error: that is the ordinary
/// host, where the key is not written at all and the daemon answers with the
/// address the control connection arrived on.
///
/// # Errors
///
/// Returns the wire error for a hostname the agent will not accept, for a port
/// outside 1-65535, for a passive address that is not a dotted-quad IPv4
/// address, and for a range whose minimum is above its maximum.
pub fn validated_ftps_configuration(
    request: &EnableFtpsRequest,
) -> Result<FtpsConfiguration, AgentError> {
    let hostname =
        Domain::parse(&request.hostname).map_err(|error| invalid_input(error.to_string()))?;
    let passive_port_min =
        Port::parse(request.passive_port_min).map_err(|error| invalid_input(error.to_string()))?;
    let passive_port_max =
        Port::parse(request.passive_port_max).map_err(|error| invalid_input(error.to_string()))?;

    if passive_port_min.value() > passive_port_max.value() {
        return Err(invalid_input(
            "the passive port range's minimum is above its maximum".to_owned(),
        ));
    }

    let passive_address = match request.passive_address.as_str() {
        "" => None,
        candidate => Some(
            PassiveAddress::parse(candidate).map_err(|error| invalid_input(error.to_string()))?,
        ),
    };

    Ok(FtpsConfiguration {
        hostname,
        passive_port_min,
        passive_port_max,
        passive_address,
        max_clients: request.max_clients,
    })
}

#[cfg(test)]
#[path = "../../tests/services/ftps/validated_ftps_configuration_tests.rs"]
mod tests;
