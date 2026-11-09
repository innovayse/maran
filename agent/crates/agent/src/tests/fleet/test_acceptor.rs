//! Test support, not a test: the server side of the fleet listener's TLS, built over a test node
//! certificate, for the tests of the accept loop and the single handshake.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;

use tokio_rustls::TlsAcceptor;

use super::fleet_server_config::fleet_server_config;
use super::node_tls_identity::NodeTlsIdentity;
use super::pin_source::PinSource;
use super::test_certificates::TestCertificate;

/// A TLS acceptor presenting `node` and admitting whatever `pin_source` pins, configured exactly
/// as the daemon configures it.
pub(crate) fn node_acceptor(node: &TestCertificate, pin_source: Arc<dyn PinSource>) -> TlsAcceptor {
    let identity = NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap();
    TlsAcceptor::from(Arc::new(fleet_server_config(identity, pin_source).unwrap()))
}
