//! Where one registered source is.

use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::port::Port;
use maran_agent_core::validation::web::remote_host::RemoteHost;

/// How to reach one registered source: a host, an SSH port and a login.
///
/// Every field is a validated type, so an endpoint that exists names a host
/// that is not a loopback, link-local, unspecified or multicast literal, a port
/// in range and a login of the allowed shape. Whether a NAME resolves to an
/// allowed address is not knowable here; `choose_source_address` asks that on
/// the resolved address.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SourceEndpoint {
    /// The DNS name or IP literal the administrator registered.
    pub host: RemoteHost,
    /// The TCP port of the source's SSH daemon.
    pub port: Port,
    /// The login on the source. A host-key scan authenticates nothing and
    /// ignores it.
    pub user: SourceUserName,
}
