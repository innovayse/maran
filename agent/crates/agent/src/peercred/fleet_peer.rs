//! What the agent knows about the far end of a fleet connection.

use std::net::SocketAddr;
use std::sync::Arc;

use rustls::pki_types::CertificateDer;

/// The far end of a fleet connection: where it connected from and the certificate chain it
/// presented in the TLS handshake.
///
/// It is a plain value on purpose. tonic's own `TlsConnectInfo` cannot be built outside tonic,
/// so a guard keyed on it could be tested only through a real handshake; this one is built from
/// it once, in [`super::agent_connection::AgentConnection`], and read by everything after.
/// It authorises nothing by itself: holding one says "this is what the peer presented", and the
/// admission service decides whether that is the pinned panel.
#[derive(Debug, Clone)]
pub struct FleetPeer {
    /// The peer's address, for the operator's log. Never an identity.
    remote_address: Option<SocketAddr>,
    /// The chain the peer presented, leaf first. `None` when the handshake carried no
    /// certificate, which the verifier refuses before a connection exists; the field is still
    /// an `Option` because the TLS library reports it as one.
    certificates: Option<Arc<Vec<CertificateDer<'static>>>>,
}

impl FleetPeer {
    /// Wraps what a finished handshake reported about the peer.
    #[must_use]
    pub fn new(
        remote_address: Option<SocketAddr>,
        certificates: Option<Arc<Vec<CertificateDer<'static>>>>,
    ) -> Self {
        Self {
            remote_address,
            certificates,
        }
    }

    /// Where the peer connected from, when the socket could say.
    #[must_use]
    pub fn remote_address(&self) -> Option<SocketAddr> {
        self.remote_address
    }

    /// The certificate the peer proved possession of the key for: the first of its chain.
    #[must_use]
    pub fn leaf_certificate(&self) -> Option<&CertificateDer<'static>> {
        self.certificates.as_ref().and_then(|chain| chain.first())
    }
}

#[cfg(test)]
#[path = "../tests/peercred/fleet_peer_tests.rs"]
mod tests;
