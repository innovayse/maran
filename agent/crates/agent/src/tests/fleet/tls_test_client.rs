//! Test support, not a test: TLS clients that dial the fleet listener with a stated certificate
//! and protocol version, so each case is about exactly one thing the listener must do.
//!
//! Mounted once for the unit tests of `fleet/` and by path for the listener's integration test.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
#![allow(dead_code, missing_docs)]

use std::net::SocketAddr;
use std::sync::Arc;

use rustls::pki_types::{CertificateDer, PrivateKeyDer, PrivatePkcs8KeyDer, ServerName};
use rustls::{ClientConfig, RootCertStore, SupportedProtocolVersion};
use tokio::net::TcpStream;
use tokio_rustls::TlsConnector;
use tokio_rustls::client::TlsStream;

use super::test_certificates::TestCertificate;

/// A connector that trusts `node` as the server, offers `client` as its own certificate when
/// there is one, speaks only `versions`, and offers h2.
pub(crate) fn tls_connector(
    node: &TestCertificate,
    client: Option<&TestCertificate>,
    versions: &[&'static SupportedProtocolVersion],
) -> TlsConnector {
    let mut roots = RootCertStore::empty();
    roots.add(CertificateDer::from(node.der.clone())).unwrap();

    let provider = Arc::new(rustls::crypto::aws_lc_rs::default_provider());
    let builder = ClientConfig::builder_with_provider(provider)
        .with_protocol_versions(versions)
        .unwrap()
        .with_root_certificates(roots);

    let mut config = match client {
        Some(client) => builder
            .with_client_auth_cert(
                vec![CertificateDer::from(client.der.clone())],
                PrivateKeyDer::Pkcs8(PrivatePkcs8KeyDer::from(client.key_der())),
            )
            .unwrap(),
        None => builder.with_no_client_auth(),
    };
    config.alpn_protocols = vec![b"h2".to_vec()];

    TlsConnector::from(Arc::new(config))
}

/// The name the test node's certificate is valid for.
pub(crate) fn node_name() -> ServerName<'static> {
    ServerName::try_from("localhost").unwrap()
}

/// Opens a TCP connection to `address` and runs the client side of the handshake over it.
/// Under TLS 1.3 this returns before the server has judged the client's certificate, so a
/// refusal shows on the first read or write, not here.
pub(crate) async fn dial(
    address: SocketAddr,
    connector: &TlsConnector,
) -> std::io::Result<TlsStream<TcpStream>> {
    let tcp = TcpStream::connect(address).await?;
    connector.connect(node_name(), tcp).await
}
