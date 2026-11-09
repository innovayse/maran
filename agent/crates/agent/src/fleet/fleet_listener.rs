//! The configuration of the fleet listener, and the step that binds it.

use std::sync::Arc;

use maran_agent_core::validation::fleet::listen_address::ListenAddress;
use tokio::net::TcpListener;
use tokio_rustls::TlsAcceptor;

use super::bound_fleet_listener::BoundFleetListener;
use super::fleet_limits::FleetLimits;
use super::fleet_listener_error::FleetListenerError;
use super::fleet_server_config::fleet_server_config;
use super::node_tls_identity::NodeTlsIdentity;
use super::pin_source::PinSource;

/// Where the listener's socket comes from.
#[derive(Debug)]
enum ListenerSocket {
    /// An address the listener binds itself, when it is bound.
    Address(ListenAddress),
    /// A socket the caller already bound; tests bind port 0 and read the port back instead of
    /// guessing a free one.
    Bound(TcpListener),
}

/// Everything the fleet listener needs, gathered before anything is bound: the address, the
/// node's identity, where the pinned panel certificate comes from, and the handshake bounds.
///
/// A daemon without fleet mode never builds one, and nothing in this module runs: the listener
/// is off unless this value is passed to `serve`.
#[derive(Debug)]
pub struct FleetListener {
    /// The socket, or the address to bind it on.
    socket: ListenerSocket,
    /// What the node presents as its own certificate.
    identity: NodeTlsIdentity,
    /// Which panel is pinned, asked on every handshake and every request.
    pin_source: Arc<dyn PinSource>,
    /// The handshake time limit and concurrency cap.
    limits: FleetLimits,
}

impl FleetListener {
    /// A listener that will bind `address` when [`FleetListener::bind`] is called.
    #[must_use]
    pub fn on_address(
        address: ListenAddress,
        identity: NodeTlsIdentity,
        pin_source: Arc<dyn PinSource>,
        limits: FleetLimits,
    ) -> Self {
        Self {
            socket: ListenerSocket::Address(address),
            identity,
            pin_source,
            limits,
        }
    }

    /// A listener over a socket the caller already bound.
    #[must_use]
    pub fn on_socket(
        socket: TcpListener,
        identity: NodeTlsIdentity,
        pin_source: Arc<dyn PinSource>,
        limits: FleetLimits,
    ) -> Self {
        Self {
            socket: ListenerSocket::Bound(socket),
            identity,
            pin_source,
            limits,
        }
    }

    /// Binds the address (when it is not bound yet) and prepares the TLS acceptor. Nothing is
    /// accepted yet.
    ///
    /// # Errors
    ///
    /// - [`FleetListenerError::Bind`] when the address cannot be bound.
    /// - [`FleetListenerError::Configuration`] or [`FleetListenerError::Identity`] when the TLS
    ///   configuration cannot be built from the node's identity.
    pub async fn bind(self) -> Result<BoundFleetListener, FleetListenerError> {
        let listener = match self.socket {
            ListenerSocket::Bound(listener) => listener,
            ListenerSocket::Address(address) => {
                let socket_address = address.socket_address();
                TcpListener::bind(socket_address).await.map_err(|source| {
                    FleetListenerError::Bind {
                        address: socket_address,
                        source,
                    }
                })?
            }
        };

        let config = fleet_server_config(self.identity, Arc::clone(&self.pin_source))?;

        // Not a failure: a node is configured before it is enrolled, and until `panel.pem`
        // exists the listener admits nobody. Said at start so the quiet is not a puzzle. The pin
        // is read first, because `current` only answers what a refresh has read.
        self.pin_source.refresh().await;
        if self.pin_source.current().is_none() {
            tracing::warn!("no panel is pinned: the fleet listener admits nobody until enrolment");
        }

        if let Ok(address) = listener.local_addr() {
            tracing::info!(
                address = %address,
                "fleet listener bound: TLS 1.3, client certificate pinned to the enrolled panel"
            );
        }

        Ok(BoundFleetListener::new(
            listener,
            TlsAcceptor::from(Arc::new(config)),
            self.pin_source,
            self.limits,
        ))
    }
}

#[cfg(test)]
#[path = "../tests/fleet/fleet_listener_tests.rs"]
mod tests;
