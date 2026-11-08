//! Tests for one bounded TLS handshake.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::time::Duration;

use rustls::version::TLS13;
use tokio::io::AsyncWriteExt;
use tokio::net::{TcpListener, TcpStream};

use super::complete_handshake;
use crate::fleet::fleet_handshake_failure::FleetHandshakeFailure;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use crate::fleet::test_acceptor::node_acceptor;
use crate::fleet::test_certificates::{valid_client_certificate, valid_server_certificate};
use crate::fleet::tls_test_client::{node_name, tls_connector};

/// Long enough that no handshake in these tests runs into it by accident.
const GENEROUS: Duration = Duration::from_secs(30);

/// A listener, a client socket connected to it, and the accepted server end.
async fn connected_pair() -> (TcpStream, TcpStream) {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let client = TcpStream::connect(listener.local_addr().unwrap())
        .await
        .unwrap();
    let (server, _peer) = listener.accept().await.unwrap();
    (client, server)
}

#[tokio::test]
async fn a_pinned_client_completes_the_handshake() {
    let node = valid_server_certificate();
    let panel = valid_client_certificate("panel");
    let pin = Arc::new(PinSlot::holding(
        PinnedPanelCertificate::from_der(panel.der.clone()).unwrap(),
    ));
    let acceptor = node_acceptor(&node, pin.clone());
    let connector = tls_connector(&node, Some(&panel), &[&TLS13]);
    let (client, server) = connected_pair().await;

    let (served, _dialled) = tokio::join!(
        complete_handshake(&acceptor, pin.as_ref(), server, GENEROUS),
        connector.connect(node_name(), client)
    );

    assert!(served.is_ok());
}

#[tokio::test]
async fn bytes_that_are_not_tls_are_refused_as_a_failed_handshake() {
    let node = valid_server_certificate();
    let pin = Arc::new(PinSlot::empty());
    let acceptor = node_acceptor(&node, pin.clone());
    let (mut client, server) = connected_pair().await;
    client.write_all(b"GET / HTTP/1.1\r\n\r\n").await.unwrap();

    let outcome = complete_handshake(&acceptor, pin.as_ref(), server, GENEROUS).await;

    assert!(matches!(outcome, Err(FleetHandshakeFailure::Refused(_))));
}

#[tokio::test]
async fn a_peer_that_sends_nothing_runs_out_of_time() {
    let node = valid_server_certificate();
    let pin = Arc::new(PinSlot::empty());
    let acceptor = node_acceptor(&node, pin.clone());
    let (_client, server) = connected_pair().await;

    let outcome =
        complete_handshake(&acceptor, pin.as_ref(), server, Duration::from_millis(100)).await;

    assert!(
        matches!(outcome, Err(FleetHandshakeFailure::TimedOut(limit)) if limit == Duration::from_millis(100))
    );
}
