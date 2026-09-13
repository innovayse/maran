//! What the panel asks the FTPS daemon to be configured with.

use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::passive_address::PassiveAddress;
use maran_agent_core::validation::web::port::Port;

/// The input of [`enable_ftps`](crate::ftps::enable_ftps): everything about the
/// daemon the panel decides.
///
/// Every field is an already-validated type rather than a `String` or a bare
/// integer, so nothing here can carry a newline into a line-oriented
/// configuration file (rules/security.md item 4) and no caller can pass the two
/// ports the wrong way round without saying so.
///
/// What is deliberately NOT here: the listening mode, which the agent probes
/// rather than accepts (see [`ListenMode`](crate::ftps::ListenMode)); the certificate
/// paths, which are derived from `hostname` inside the agent's own store; and
/// the control port, which is 21 and is not negotiable — it is the one port an
/// operator has already opened and already understands.
#[derive(Debug, Clone)]
pub struct FtpsConfiguration {
    /// The hostname the daemon serves, and the name its certificate material is
    /// filed under.
    pub hostname: Domain,
    /// Lowest port of the passive data range, inclusive.
    ///
    /// The range is fixed rather than negotiated because the firewall has to be
    /// told about it in advance: the kernel's FTP conntrack helper, which would
    /// open these dynamically, reads the `PASV` reply off a control channel this
    /// design keeps encrypted.
    pub passive_port_min: Port,
    /// Highest port of the passive data range, inclusive.
    pub passive_port_max: Port,
    /// The address to advertise in the `PASV` reply, for a host behind NAT whose
    /// public address is not the one the socket is bound to.
    ///
    /// `None` on the ordinary host, where the key is not written at all and
    /// vsftpd answers with the address the control connection arrived on.
    pub passive_address: Option<PassiveAddress>,
    /// The daemon's concurrent-session ceiling.
    pub max_clients: u32,
}
