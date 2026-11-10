//! Reading a `SourceEndpoint` into validated input, or refusing it.

use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;
use maran_agent_core::validation::web::remote_host_error::RemoteHostError;
use maran_ops::transfer::model::source_endpoint::SourceEndpoint;

use crate::proto;
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::wire::invalid_input::invalid_input;

/// Revalidates the host, port and login a migration rpc carries.
///
/// The panel validated them already; this is the agent's own check, and it matters
/// more here than anywhere else in the contract because the agent is about to
/// open an outbound connection to whatever passes. Each of the three values is
/// built through its own type, so a leading `-` (an ssh option), a scheme, a
/// space, a control character or a port outside `1..=65535` never reaches an
/// argument vector.
///
/// A host that is a literal loopback, link-local, unspecified or multicast address
/// (or `localhost`) is refused with the connection reason `ADDRESS_REFUSED`, the
/// same reason a name that RESOLVES to one is refused with later: the panel tells
/// its operator one thing for both. The login is revalidated for `ScanHostKey` too,
/// although the scan never authenticates and never sends it, because the panel
/// always has the registered login at hand and one endpoint type is one rule.
///
/// # Errors
///
/// `INVALID_INPUT` for an absent endpoint, a host that is not a host name or an IP
/// address, a port outside the range and a login outside the grammar; the same
/// code with the reason `ADDRESS_REFUSED` for a forbidden address literal.
pub fn validated_source_endpoint(
    endpoint: Option<proto::SourceEndpoint>,
) -> Result<SourceEndpoint, SourceFailure> {
    let endpoint = endpoint
        .ok_or_else(|| SourceFailure::plain(invalid_input("no source endpoint".to_owned())))?;

    let host = RemoteHost::parse(&endpoint.host).map_err(|error| {
        let refusal = invalid_input(error.to_string());
        match error {
            RemoteHostError::ForbiddenAddress | RemoteHostError::LocalName => {
                SourceFailure::address_refused(refusal)
            }
            _ => SourceFailure::plain(refusal),
        }
    })?;
    let port = Port::parse(endpoint.port)
        .map_err(|error| SourceFailure::plain(invalid_input(error.to_string())))?;
    let user = SourceUserName::parse(&endpoint.user)
        .map_err(|error| SourceFailure::plain(invalid_input(error.to_string())))?;

    Ok(SourceEndpoint { host, port, user })
}

#[cfg(test)]
#[path = "../../tests/services/transfer/validated_source_endpoint_tests.rs"]
mod tests;
