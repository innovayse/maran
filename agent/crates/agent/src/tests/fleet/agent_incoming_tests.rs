//! Tests for the stream that merges the unix socket's connections with the fleet listener's.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::time::Duration;

use rustls::version::TLS13;
use tokio::net::{TcpListener, UnixListener, UnixStream};
use tokio_stream::StreamExt;

use super::agent_incoming;
use crate::fleet::fleet_limits::FleetLimits;
use crate::fleet::fleet_listener::FleetListener;
use crate::fleet::node_tls_identity::NodeTlsIdentity;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use crate::fleet::test_certificates::{valid_client_certificate, valid_server_certificate};
use crate::fleet::tls_test_client::{dial, tls_connector};
use crate::peercred::AgentConnection;

/// How long a test waits for a connection that must arrive.
const PATIENCE: Duration = Duration::from_secs(20);

/// How long a test waits to be sure something does NOT arrive.
const QUIET: Duration = Duration::from_millis(300);

#[tokio::test]
async fn without_a_fleet_listener_only_unix_connections_arrive() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("agent.sock");
    let mut incoming = Box::pin(agent_incoming(UnixListener::bind(&path).unwrap(), None));
    let _client = UnixStream::connect(&path).await.unwrap();

    let first = tokio::time::timeout(PATIENCE, incoming.next())
        .await
        .unwrap();
    let second = tokio::time::timeout(QUIET, incoming.next()).await;

    assert!(matches!(first, Some(Ok(AgentConnection::Unix(_)))));
    assert!(
        second.is_err(),
        "nothing else may arrive, and the stream must not have ended"
    );
}

#[tokio::test]
async fn with_a_fleet_listener_both_kinds_of_connection_arrive_on_one_stream() {
    let directory = tempfile::tempdir().unwrap();
    let path = directory.path().join("agent.sock");
    let node = valid_server_certificate();
    let panel = valid_client_certificate("panel");
    let socket = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = socket.local_addr().unwrap();
    let bound = FleetListener::on_socket(
        socket,
        NodeTlsIdentity::from_pem(&node.pem, &node.key_pem()).unwrap(),
        Arc::new(PinSlot::holding(
            PinnedPanelCertificate::from_der(panel.der.clone()).unwrap(),
        )),
        FleetLimits::standard(),
    )
    .bind()
    .await
    .unwrap();
    let mut incoming = Box::pin(agent_incoming(
        UnixListener::bind(&path).unwrap(),
        Some(bound),
    ));

    let _unix_client = UnixStream::connect(&path).await.unwrap();
    let _fleet_client = dial(address, &tls_connector(&node, Some(&panel), &[&TLS13]))
        .await
        .unwrap();

    let mut saw_unix = false;
    let mut saw_fleet = false;
    for _ in 0..2 {
        match tokio::time::timeout(PATIENCE, incoming.next())
            .await
            .unwrap()
        {
            Some(Ok(AgentConnection::Unix(_))) => saw_unix = true,
            Some(Ok(AgentConnection::Fleet(_))) => saw_fleet = true,
            other => panic!("unexpected item: {:?}", other.map(|item| item.map(|_| ()))),
        }
    }

    assert!(saw_unix && saw_fleet);
}
