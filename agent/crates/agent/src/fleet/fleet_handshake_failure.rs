//! Why a fleet connection never became a session.

use std::time::Duration;

/// How a TLS handshake on the fleet listener ended without a session.
///
/// Text for the operator's log (the warn line of a refused connection); nothing here reaches a
/// peer, which sees only a closed connection or a TLS alert.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum FleetHandshakeFailure {
    /// The handshake was started and refused: no client certificate, a certificate that is not
    /// the pinned one, a protocol below TLS 1.3, a client that does not speak h2, or garbage.
    #[error("the TLS handshake was refused: {0}")]
    Refused(std::io::Error),
    /// The peer connected and did not finish the handshake in time.
    #[error("the TLS handshake did not finish within {0:?}")]
    TimedOut(Duration),
}
