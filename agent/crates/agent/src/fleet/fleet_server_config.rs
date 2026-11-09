//! The TLS configuration of the fleet listener.

use std::sync::Arc;

use rustls::ServerConfig;
use rustls::server::NoServerSessionStorage;

use super::fleet_crypto_provider::fleet_crypto_provider;
use super::fleet_listener_error::FleetListenerError;
use super::node_tls_identity::NodeTlsIdentity;
use super::pin_source::PinSource;
use super::pinned_client_verifier::PinnedClientVerifier;

/// The ALPN protocol gRPC runs on. A client that does not offer it is refused in the handshake.
const ALPN_HTTP2: &[u8] = b"h2";

/// Builds the server side of the fleet listener's TLS.
///
/// - **TLS 1.3 only.** The version list is passed explicitly; tonic's own TLS configuration
///   cannot restrict it and would also accept 1.2.
/// - **A client certificate is mandatory** and checked by [`PinnedClientVerifier`]; there is no
///   unauthenticated mode.
/// - **No session resumption.** The in-memory ticket cache and the tickets themselves are
///   switched off, because a resumed TLS 1.3 session restores the certificate chain of the first
///   one without running the verifier again: a pin removed after a ticket was issued would keep
///   admitting the old client. The panel keeps a few long-lived connections, so resumption buys
///   nothing.
/// - **No early data.** The default of zero is left, stated here because 0-RTT is a replay
///   surface a root daemon must not have.
///
/// # Errors
///
/// - [`FleetListenerError::Configuration`] when the provider cannot do TLS 1.3.
/// - [`FleetListenerError::Identity`] when the certificate and key do not form a usable server
///   identity (for instance, the key is not the certificate's).
pub fn fleet_server_config(
    identity: NodeTlsIdentity,
    pin_source: Arc<dyn PinSource>,
) -> Result<ServerConfig, FleetListenerError> {
    let provider = fleet_crypto_provider();
    let verifier = Arc::new(PinnedClientVerifier::new(pin_source, Arc::clone(&provider)));
    let (chain, key) = identity.into_parts();

    let mut config = ServerConfig::builder_with_provider(provider)
        .with_protocol_versions(&[&rustls::version::TLS13])
        .map_err(FleetListenerError::Configuration)?
        .with_client_cert_verifier(verifier)
        .with_single_cert(chain, key)
        .map_err(FleetListenerError::Identity)?;

    config.alpn_protocols = vec![ALPN_HTTP2.to_vec()];
    config.session_storage = Arc::new(NoServerSessionStorage {});
    config.send_tls13_tickets = 0;
    config.max_early_data_size = 0;

    Ok(config)
}

#[cfg(test)]
#[path = "../tests/fleet/fleet_server_config_tests.rs"]
mod tests;
