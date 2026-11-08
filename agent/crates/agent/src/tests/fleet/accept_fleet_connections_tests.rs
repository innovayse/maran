//! Tests for the accept loop of the fleet listener, over real loopback sockets and real TLS.
//!
//! Each refusal sits beside an admitted connection made the same way, so a loop that refused
//! everything would fail a control rather than pass the refusals.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use rustls::version::{TLS12, TLS13};
use tokio::io::AsyncReadExt;
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::mpsc;

use super::accept_fleet_connections;
use crate::fleet::fleet_limits::FleetLimits;
use crate::fleet::pin_slot::PinSlot;
use crate::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use crate::fleet::test_acceptor::node_acceptor;
use crate::fleet::test_certificates::{
    TestCertificate, valid_client_certificate, valid_server_certificate,
};
use crate::fleet::tls_test_client::{dial, tls_connector};
use crate::peercred::AgentConnection;

/// How long a test waits for something that must happen. Generous: it is only ever spent when a
/// test is about to fail.
const PATIENCE: Duration = Duration::from_secs(20);

/// A running accept loop, the node and panel certificates it was built over, and the end the
/// server would take sessions from.
struct Loop {
    /// Where to connect.
    address: SocketAddr,
    /// What the node presents.
    node: TestCertificate,
    /// The panel the node pinned.
    panel: TestCertificate,
    /// The sessions that completed their handshake.
    sessions: mpsc::Receiver<AgentConnection>,
    /// The loop's task.
    task: tokio::task::JoinHandle<()>,
}

/// Starts an accept loop under `limits`, pinning a fresh panel certificate.
async fn start(limits: FleetLimits) -> Loop {
    let node = valid_server_certificate();
    let panel = valid_client_certificate("panel");
    let slot = Arc::new(PinSlot::holding(
        PinnedPanelCertificate::from_der(panel.der.clone()).unwrap(),
    ));
    let acceptor = node_acceptor(&node, slot.clone());

    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let (sender, sessions) = mpsc::channel(4);
    let task = tokio::spawn(accept_fleet_connections(
        listener, acceptor, slot, limits, sender,
    ));

    Loop {
        address,
        node,
        panel,
        sessions,
        task,
    }
}

fn roomy_limits() -> FleetLimits {
    FleetLimits::new(Duration::from_secs(60), 8)
}

/// Whether the listener turned the client away: either the handshake failed outright, or it
/// looked finished (TLS 1.3 lets the client finish first) and the first read showed the server
/// had closed the connection or sent an alert.
async fn is_turned_away(address: SocketAddr, connector: &tokio_rustls::TlsConnector) -> bool {
    match dial(address, connector).await {
        Err(_) => true,
        Ok(mut session) => {
            let mut buffer = [0_u8; 1];
            let read = tokio::time::timeout(PATIENCE, session.read(&mut buffer))
                .await
                .unwrap();
            matches!(read, Ok(0) | Err(_))
        }
    }
}

#[tokio::test]
async fn the_pinned_panel_completes_the_handshake_and_its_session_is_handed_over() {
    let mut fleet = start(roomy_limits()).await;
    let connector = tls_connector(&fleet.node, Some(&fleet.panel), &[&TLS13]);

    let _session = dial(fleet.address, &connector).await.unwrap();

    let handed_over = tokio::time::timeout(PATIENCE, fleet.sessions.recv())
        .await
        .unwrap();
    assert!(matches!(handed_over, Some(AgentConnection::Fleet(_))));
}

#[tokio::test]
async fn a_client_with_no_certificate_is_refused_and_nothing_is_handed_over() {
    let mut fleet = start(roomy_limits()).await;
    let connector = tls_connector(&fleet.node, None, &[&TLS13]);

    assert!(is_turned_away(fleet.address, &connector).await);
    assert!(fleet.sessions.try_recv().is_err());
}

#[tokio::test]
async fn a_different_certificate_is_refused_and_nothing_is_handed_over() {
    let mut fleet = start(roomy_limits()).await;
    let impostor = valid_client_certificate("panel");
    let connector = tls_connector(&fleet.node, Some(&impostor), &[&TLS13]);

    assert!(is_turned_away(fleet.address, &connector).await);
    assert!(fleet.sessions.try_recv().is_err());
}

#[tokio::test]
async fn a_tls_1_2_client_is_refused() {
    let mut fleet = start(roomy_limits()).await;
    let connector = tls_connector(&fleet.node, Some(&fleet.panel), &[&TLS12]);

    let outcome = dial(fleet.address, &connector).await;

    assert!(outcome.is_err(), "the listener must not speak TLS 1.2");
    assert!(fleet.sessions.try_recv().is_err());
    // The control: the same client over TLS 1.3 is served, so the refusal above was the version.
    let current = tls_connector(&fleet.node, Some(&fleet.panel), &[&TLS13]);
    let _session = dial(fleet.address, &current).await.unwrap();
    let handed_over = tokio::time::timeout(PATIENCE, fleet.sessions.recv())
        .await
        .unwrap();
    assert!(matches!(handed_over, Some(AgentConnection::Fleet(_))));
}

#[tokio::test]
async fn a_peer_that_connects_and_says_nothing_is_dropped_when_the_handshake_time_runs_out() {
    let fleet = start(FleetLimits::new(Duration::from_millis(300), 8)).await;
    let mut idle = TcpStream::connect(fleet.address).await.unwrap();

    let mut buffer = [0_u8; 1];
    let read = tokio::time::timeout(PATIENCE, idle.read(&mut buffer))
        .await
        .expect("the server must close the idle connection once the handshake time is up");

    assert!(matches!(read, Ok(0) | Err(_)));
}

#[tokio::test]
async fn a_connection_over_the_handshake_cap_is_closed_and_the_cap_frees_when_one_ends() {
    let mut fleet = start(FleetLimits::new(Duration::from_secs(60), 1)).await;
    // The first connection occupies the only slot by saying nothing.
    let holder = TcpStream::connect(fleet.address).await.unwrap();
    // Connections are accepted in the order they were made, so the second one meets a full cap.
    let mut refused = TcpStream::connect(fleet.address).await.unwrap();

    let mut buffer = [0_u8; 1];
    let read = tokio::time::timeout(PATIENCE, refused.read(&mut buffer))
        .await
        .unwrap();
    assert!(
        matches!(read, Ok(0) | Err(_)),
        "the second connection must be closed"
    );

    // The control: once the holder is gone its handshake fails and the slot is released, so a
    // real client is served. The release happens in the loop's own task, so the client tries
    // again until it is; a cap that never freed would exhaust PATIENCE here.
    drop(holder);
    let connector = tls_connector(&fleet.node, Some(&fleet.panel), &[&TLS13]);
    let served = tokio::time::timeout(PATIENCE, async {
        loop {
            if let Ok(session) = dial(fleet.address, &connector).await {
                let handed_over =
                    tokio::time::timeout(Duration::from_secs(1), fleet.sessions.recv()).await;
                if matches!(handed_over, Ok(Some(AgentConnection::Fleet(_)))) {
                    return session;
                }
            }
            tokio::time::sleep(Duration::from_millis(20)).await;
        }
    })
    .await;
    assert!(served.is_ok(), "the slot must free once the holder is gone");
}

#[tokio::test]
async fn the_loop_ends_when_the_server_stops_taking_sessions() {
    let Loop { sessions, task, .. } = start(roomy_limits()).await;

    drop(sessions);

    tokio::time::timeout(PATIENCE, task).await.unwrap().unwrap();
}
