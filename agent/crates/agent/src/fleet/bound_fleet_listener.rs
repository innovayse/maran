//! A fleet listener that owns its socket and has not yet started accepting.

use std::io;
use std::net::SocketAddr;
use std::sync::Arc;

use tokio::net::TcpListener;
use tokio::sync::mpsc;
use tokio_rustls::TlsAcceptor;
use tokio_stream::wrappers::ReceiverStream;

use super::accept_fleet_connections::accept_fleet_connections;
use super::fleet_admission_layer::FleetAdmissionLayer;
use super::fleet_limits::FleetLimits;
use super::pin_source::PinSource;
use crate::peercred::AgentConnection;

/// How many finished sessions may wait for the server to take them. The server takes them
/// immediately; the bound only stops a stalled server from letting admitted sessions pile up.
const HAND_OFF_CAPACITY: usize = 16;

/// The fleet listener after its address is bound and before it accepts: the socket, the TLS
/// acceptor built from the node's identity and the pin source.
///
/// Binding is a separate step from accepting for the reason the unix socket's is: the daemon
/// binds both listeners before it runs anything that must not race a request, and accepts on
/// both only when the server starts.
pub struct BoundFleetListener {
    /// The bound TCP socket.
    listener: TcpListener,
    /// Completes the TLS handshake of each accepted socket.
    acceptor: TlsAcceptor,
    /// Where the admission layer reads the pin from.
    pin_source: Arc<dyn PinSource>,
    /// The handshake bounds.
    limits: FleetLimits,
}

impl BoundFleetListener {
    /// Assembles a bound listener. Called by [`super::fleet_listener::FleetListener`].
    pub(super) fn new(
        listener: TcpListener,
        acceptor: TlsAcceptor,
        pin_source: Arc<dyn PinSource>,
        limits: FleetLimits,
    ) -> Self {
        Self {
            listener,
            acceptor,
            pin_source,
            limits,
        }
    }

    /// The address the socket is bound to; the actual port when 0 was asked for.
    ///
    /// # Errors
    ///
    /// Returns the operating system's error when the socket cannot report its address.
    pub fn local_address(&self) -> io::Result<SocketAddr> {
        self.listener.local_addr()
    }

    /// The layer that admits only requests from the pinned panel, to be installed on the same
    /// server this listener feeds.
    #[must_use]
    pub fn admission_layer(&self) -> FleetAdmissionLayer {
        FleetAdmissionLayer::new(Arc::clone(&self.pin_source))
    }

    /// Starts accepting and returns the sessions that completed their handshake, as the stream
    /// the server consumes.
    ///
    /// The accept loop runs as its own task and ends, closing the socket, when the returned
    /// stream is dropped.
    #[must_use]
    pub fn accept_connections(self) -> ReceiverStream<AgentConnection> {
        let (sender, receiver) = mpsc::channel(HAND_OFF_CAPACITY);
        tokio::spawn(accept_fleet_connections(
            self.listener,
            self.acceptor,
            self.pin_source,
            self.limits,
            sender,
        ));
        ReceiverStream::new(receiver)
    }
}
