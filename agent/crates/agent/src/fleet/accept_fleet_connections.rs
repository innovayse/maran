//! The accept loop of the fleet listener.

use std::sync::Arc;
use std::time::Duration;

use tokio::net::TcpListener;
use tokio::sync::{Semaphore, mpsc};
use tokio_rustls::TlsAcceptor;

use super::complete_handshake::complete_handshake;
use super::fleet_limits::FleetLimits;
use super::pin_source::PinSource;
use crate::peercred::AgentConnection;

/// How long the loop waits after `accept` itself failed (descriptor exhaustion is the usual
/// cause) before trying again, so a persistent failure is not a busy loop.
const ACCEPT_FAILURE_BACKOFF: Duration = Duration::from_millis(100);

/// Accepts TCP connections on `listener` until `connections` is dropped, completes each peer's
/// TLS handshake under `limits` (refreshing `pin_source` first, inside the same time limit), and
/// hands every finished session to `connections`.
///
/// A connection that arrives while `limits` handshakes are already running is closed without a
/// handshake; one whose handshake is refused or runs out of time is dropped. Both are logged at
/// `warn` with the peer's address, which is the only visibility the operator has of a scanner or
/// of a panel whose certificate was replaced. Only a session that completed the mutual
/// handshake reaches the server; everything else ends here.
///
/// The loop stops, and the listener closes, when the receiving side is dropped, which is what
/// happens when the server shuts down.
pub(super) async fn accept_fleet_connections(
    listener: TcpListener,
    acceptor: TlsAcceptor,
    pin_source: Arc<dyn PinSource>,
    limits: FleetLimits,
    connections: mpsc::Sender<AgentConnection>,
) {
    let handshakes = Arc::new(Semaphore::new(limits.max_concurrent_handshakes()));

    loop {
        let accepted = tokio::select! {
            () = connections.closed() => return,
            accepted = listener.accept() => accepted,
        };

        let (connection, peer) = match accepted {
            Ok(accepted) => accepted,
            Err(error) => {
                tracing::warn!(%error, "the fleet listener could not accept a connection");
                tokio::time::sleep(ACCEPT_FAILURE_BACKOFF).await;
                continue;
            }
        };

        let Ok(permit) = Arc::clone(&handshakes).try_acquire_owned() else {
            tracing::warn!(
                %peer,
                limit = limits.max_concurrent_handshakes(),
                "a fleet connection was refused: too many handshakes are already in flight"
            );
            continue;
        };

        // Best effort: gRPC frames are small and latency-bound.
        let _ = connection.set_nodelay(true);

        let acceptor = acceptor.clone();
        let pin_source = Arc::clone(&pin_source);
        let connections = connections.clone();
        tokio::spawn(async move {
            let outcome = complete_handshake(
                &acceptor,
                pin_source.as_ref(),
                connection,
                limits.handshake_timeout(),
            )
            .await;
            // The permit bounds handshakes, not sessions: it is released as soon as this one
            // is over, so an admitted panel connection does not use up the cap.
            drop(permit);

            match outcome {
                Ok(session) => {
                    tracing::info!(%peer, "a fleet peer completed the TLS handshake");
                    // A failure means the server is gone; the session is closed by the drop.
                    let _ = connections
                        .send(AgentConnection::Fleet(Box::new(session)))
                        .await;
                }
                Err(failure) => {
                    tracing::warn!(%peer, %failure, "a fleet connection was refused");
                }
            }
        });
    }
}

#[cfg(test)]
#[path = "../tests/fleet/accept_fleet_connections_tests.rs"]
mod tests;
