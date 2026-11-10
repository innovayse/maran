//! Everything the one `ssh` argv builder needs about a connection.

use std::net::IpAddr;
use std::path::PathBuf;

use maran_agent_core::validation::system::source_user_name::SourceUserName;
use maran_agent_core::validation::web::port::Port;

/// One authenticated connection to a source, resolved and with its files laid
/// out: the literal address to connect to, the login, the port, the private key
/// file and the known_hosts file the agent wrote, and the host key algorithm the
/// pinned key is of.
#[derive(Debug, Clone)]
pub struct SshConnection {
    /// The address the name resolved to once. Never re-resolved.
    pub address: IpAddr,
    /// The source's SSH port.
    pub port: Port,
    /// The login on the source.
    pub user: SourceUserName,
    /// The root-only file holding the private key.
    pub key_file: PathBuf,
    /// The root-only file holding the pinned host key.
    pub known_hosts_file: PathBuf,
    /// The algorithm of the pinned host key (`ssh-ed25519`, ...), which decides
    /// the one host key algorithm the client is allowed to negotiate.
    pub host_key_algorithm: String,
}
