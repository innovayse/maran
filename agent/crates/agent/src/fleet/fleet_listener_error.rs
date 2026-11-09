//! Why the fleet listener could not be set up.

use std::net::SocketAddr;

/// Errors raised while the fleet listener is being prepared, before it accepts a connection.
///
/// Every variant is fatal at start: a node that was asked to join a fleet and cannot listen is
/// a node that reports itself reachable to nobody, and the operator must be told at the
/// start-up, not discover it when the panel cannot dial.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum FleetListenerError {
    /// The TCP address could not be bound (in use, or not an address of this host).
    #[error("cannot bind the fleet listener on {address}: {source}")]
    Bind {
        /// The address that was asked for.
        address: SocketAddr,
        /// The operating system's reason.
        source: std::io::Error,
    },
    /// The node's identity files could not be read: the fleet directory or one of its files is
    /// missing, too open, or not what it should be.
    #[error("the fleet identity cannot be read: {0}")]
    IdentityFiles(super::fleet_identity_error::FleetIdentityError),
    /// A certificate or key given as PEM text could not be read: no block of the expected kind,
    /// or a block that is not valid base64.
    #[error("the PEM text could not be read: {0}")]
    Pem(rustls::pki_types::pem::Error),
    /// The TLS library refused to be configured for TLS 1.3 with the agent's crypto provider.
    #[error("the fleet listener's TLS configuration is invalid: {0}")]
    Configuration(rustls::Error),
    /// The node's certificate chain and private key do not form a usable server identity
    /// (a key that does not match the certificate, or a key of a kind the provider cannot sign
    /// with).
    #[error("the node identity is not usable as a TLS server identity: {0}")]
    Identity(rustls::Error),
    /// The pinned panel certificate could not be turned into a trust anchor: it is not a
    /// certificate the TLS library can parse.
    #[error("the pinned panel certificate is not usable: {0}")]
    PinnedCertificate(rustls::Error),
    /// The pinned panel certificate could not be turned into a client verifier.
    #[error("the pinned panel certificate cannot verify a client: {0}")]
    PinnedVerifier(String),
}
