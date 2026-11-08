//! One TLS handshake on the fleet listener, bounded in time.

use std::time::Duration;

use tokio::net::TcpStream;
use tokio_rustls::TlsAcceptor;
use tokio_rustls::server::TlsStream;

use super::fleet_handshake_failure::FleetHandshakeFailure;
use super::pin_source::PinSource;

/// Runs the server side of the TLS handshake on `connection`, giving up after `limit`.
///
/// The pin is refreshed first, so the verifier, which can only read memory, judges the client
/// against the pin as it is now. The limit covers the refresh and the whole handshake from the
/// first byte of waiting, including a client that
/// opens the socket and sends nothing. It is the reason an idle connection costs a descriptor
/// and a task for `limit` and not for as long as the kernel keeps it.
///
/// # Errors
///
/// - [`FleetHandshakeFailure::Refused`] when the TLS library refused the peer.
/// - [`FleetHandshakeFailure::TimedOut`] when `limit` passed first.
pub(super) async fn complete_handshake(
    acceptor: &TlsAcceptor,
    pin_source: &dyn PinSource,
    connection: TcpStream,
    limit: Duration,
) -> Result<TlsStream<TcpStream>, FleetHandshakeFailure> {
    let handshake = async {
        pin_source.refresh().await;
        acceptor.accept(connection).await
    };

    match tokio::time::timeout(limit, handshake).await {
        Ok(Ok(session)) => Ok(session),
        Ok(Err(error)) => Err(FleetHandshakeFailure::Refused(error)),
        Err(_elapsed) => Err(FleetHandshakeFailure::TimedOut(limit)),
    }
}

#[cfg(test)]
#[path = "../tests/fleet/complete_handshake_tests.rs"]
mod tests;
