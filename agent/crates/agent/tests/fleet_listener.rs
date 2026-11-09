//! The fleet listener on the real server (issue #180): a node started in this process with its
//! TCP + mutual-TLS listener on, dialled over real loopback sockets by real TLS clients and by
//! the generated gRPC client.
//!
//! What this proves: only the pinned panel certificate is served; a client with no certificate,
//! with a different one, with a re-issued one or speaking TLS 1.2 or no TLS is not; a pin removed
//! while a connection is open stops that connection's next request; refusals are logged at
//! `warn`; the unix socket keeps working beside the listener and while it is flooded; and a
//! daemon started without the listener has none.
//!
//! What it cannot: that a .NET panel's `SslStream` interoperates (the panel side is proved on a
//! real host, by the polygon run), that the port is reachable through a real firewall, and how
//! the listener behaves under SELinux enforcing. Every request below is a read-only
//! `GetAgentInfo`, so no test needs root or a database.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/captured_log.rs"]
mod captured_log;
#[path = "../src/tests/fleet/test_certificates.rs"]
mod test_certificates;
#[path = "../src/tests/fleet/tls_test_client.rs"]
mod tls_test_client;

use std::net::SocketAddr;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{Duration, Instant};

use hyper_util::rt::TokioIo;
use rustls::SupportedProtocolVersion;
use rustls::version::{TLS12, TLS13};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream, UnixStream};
use tonic::Code;
use tonic::transport::{Channel, Endpoint, Uri};
use tower::service_fn;

use maran_agent::error::StartupError;
use maran_agent::fleet::fleet_limits::FleetLimits;
use maran_agent::fleet::fleet_listener::FleetListener;
use maran_agent::fleet::node_tls_identity::NodeTlsIdentity;
use maran_agent::fleet::pin_slot::PinSlot;
use maran_agent::fleet::pinned_panel_certificate::PinnedPanelCertificate;
use maran_agent::peercred::PeerPolicy;
use maran_agent::proto::GetAgentInfoRequest;
use maran_agent::proto::get_agent_info_response;
use maran_agent::proto::system_service_client::SystemServiceClient;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

use captured_log::CapturedLog;
use test_certificates::{TestCertificate, valid_client_certificate, valid_server_certificate};
use tls_test_client::{dial, tls_connector};

/// How long a test waits for something that must happen.
const PATIENCE: Duration = Duration::from_secs(20);

/// Gap between two looks while waiting.
const POLL_INTERVAL: Duration = Duration::from_millis(20);

/// Authority the endpoints are built with; the connectors below ignore it.
const UNUSED_AUTHORITY: &str = "http://localhost";

/// A node started in this process, with the certificates its listener was built over.
struct Node {
    /// The unix socket the server binds.
    socket_path: PathBuf,
    /// The address of the fleet listener, when it was started.
    fleet_address: Option<SocketAddr>,
    /// What the node presents as its own certificate.
    node_certificate: TestCertificate,
    /// The panel the node pinned.
    panel: TestCertificate,
    /// The pin, so a test can remove or restore it while the server runs.
    pin: Arc<PinSlot>,
    /// The server task, aborted on drop.
    server: tokio::task::JoinHandle<Result<(), StartupError>>,
    /// Owns the directory holding the socket.
    _directory: tempfile::TempDir,
}

impl Drop for Node {
    /// Stops the server task.
    fn drop(&mut self) {
        self.server.abort();
    }
}

/// Starts a node with or without the fleet listener. `None` means this host is outside the
/// support matrix, so there is nothing to test; the skip is printed where CI can see it.
async fn start_node(with_fleet: bool, limits: FleetLimits) -> Option<Node> {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let node_certificate = valid_server_certificate();
    let panel = valid_client_certificate("panel");
    let pin = Arc::new(PinSlot::holding(
        PinnedPanelCertificate::from_der(panel.der.clone()).unwrap(),
    ));

    let (fleet, fleet_address) = if with_fleet {
        let socket = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = socket.local_addr().unwrap();
        let identity =
            NodeTlsIdentity::from_pem(&node_certificate.pem, &node_certificate.key_pem()).unwrap();
        (
            Some(FleetListener::on_socket(
                socket,
                identity,
                pin.clone(),
                limits,
            )),
            Some(address),
        )
    } else {
        (None, None)
    };

    let policy = PeerPolicy::new(maran_agent_core::utils::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server = tokio::spawn(async move {
        maran_agent::server::serve(&server_path, policy, FreeSpaceFloor::default_floor(), fleet)
            .await
    });

    let deadline = Instant::now() + PATIENCE;
    loop {
        if socket_path.exists() {
            break;
        }
        if server.is_finished() {
            match (&mut server).await.unwrap() {
                Err(StartupError::Distro(error)) => {
                    eprintln!("SKIPPED the fleet listener test: {error}");
                    return None;
                }
                other => panic!("the server stopped before binding: {other:?}"),
            }
        }
        assert!(
            Instant::now() < deadline,
            "the server did not bind its socket"
        );
        tokio::time::sleep(POLL_INTERVAL).await;
    }

    Some(Node {
        socket_path,
        fleet_address,
        node_certificate,
        panel,
        pin,
        server,
        _directory: directory,
    })
}

/// A gRPC channel over the unix socket.
async fn unix_channel(socket_path: &Path) -> Channel {
    let path = socket_path.to_path_buf();
    Endpoint::try_from(UNUSED_AUTHORITY)
        .unwrap()
        .connect_with_connector(service_fn(move |_: Uri| {
            let path = path.clone();
            async move { Ok::<_, std::io::Error>(TokioIo::new(UnixStream::connect(path).await?)) }
        }))
        .await
        .unwrap()
}

/// A gRPC channel over mutual TLS to the fleet listener. A client the server turns away shows as
/// an error here or on the first call.
async fn fleet_channel(
    node: &Node,
    client: Option<&TestCertificate>,
    versions: &[&'static SupportedProtocolVersion],
) -> Result<Channel, tonic::transport::Error> {
    let connector = tls_connector(&node.node_certificate, client, versions);
    let address = node.fleet_address.unwrap();
    Endpoint::try_from(UNUSED_AUTHORITY)
        .unwrap()
        .connect_with_connector(service_fn(move |_: Uri| {
            let connector = connector.clone();
            async move { Ok::<_, std::io::Error>(TokioIo::new(dial(address, &connector).await?)) }
        }))
        .await
}

/// Asks `GetAgentInfo` through `channel` and says whether an agent answered; a refused
/// call is reported by the status code alone, which is all a caller here reads.
async fn agent_answers(channel: Channel) -> Result<bool, Code> {
    let response = SystemServiceClient::new(channel)
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .map_err(|status| status.code())?
        .into_inner();
    Ok(matches!(
        response.result,
        Some(get_agent_info_response::Result::Ok(_))
    ))
}

/// Whether a client of this kind got an answer from the agent at all.
async fn served(
    node: &Node,
    client: Option<&TestCertificate>,
    versions: &[&'static SupportedProtocolVersion],
) -> bool {
    match fleet_channel(node, client, versions).await {
        Ok(channel) => agent_answers(channel).await.unwrap_or(false),
        Err(_) => false,
    }
}

#[tokio::test]
async fn the_pinned_panel_is_served_over_mutual_tls() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };

    assert!(served(&node, Some(&node.panel), &[&TLS13]).await);
}

#[tokio::test]
async fn a_client_with_no_certificate_is_not_served() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };

    assert!(!served(&node, None, &[&TLS13]).await);
    // The control: the same node serves the pinned panel in the same way.
    assert!(served(&node, Some(&node.panel), &[&TLS13]).await);
}

#[tokio::test]
async fn a_different_certificate_is_not_served() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };
    let impostor = valid_client_certificate("panel");

    assert!(!served(&node, Some(&impostor), &[&TLS13]).await);
    assert!(served(&node, Some(&node.panel), &[&TLS13]).await);
}

#[tokio::test]
async fn the_pinned_key_certified_again_is_not_served() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };
    let reissued = node.panel.reissued();

    assert!(!served(&node, Some(&reissued), &[&TLS13]).await);
    assert!(served(&node, Some(&node.panel), &[&TLS13]).await);
}

#[tokio::test]
async fn a_tls_1_2_client_is_not_served() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };

    assert!(!served(&node, Some(&node.panel), &[&TLS12]).await);
    assert!(served(&node, Some(&node.panel), &[&TLS13]).await);
}

#[tokio::test]
async fn a_client_that_speaks_no_tls_gets_no_grpc() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };
    let mut plain = TcpStream::connect(node.fleet_address.unwrap())
        .await
        .unwrap();

    // The HTTP/2 connection preface, which is what a plaintext gRPC client would send.
    plain
        .write_all(b"PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n")
        .await
        .unwrap();
    let mut received = Vec::new();
    let finished = tokio::time::timeout(PATIENCE, plain.read_to_end(&mut received)).await;

    assert!(
        finished.is_ok(),
        "the server must close a connection that is not TLS"
    );
    assert!(
        received.is_empty() || received[0] == 0x15,
        "all the server may send back is a TLS alert, got {received:?}"
    );
}

#[tokio::test]
async fn removing_the_pin_stops_an_open_connection_at_its_next_request() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };
    let channel = fleet_channel(&node, Some(&node.panel), &[&TLS13])
        .await
        .unwrap();
    assert!(
        agent_answers(channel.clone()).await.unwrap(),
        "control: admitted while pinned"
    );

    node.pin.replace(None);
    let refused = agent_answers(channel.clone()).await.unwrap_err();
    assert_eq!(refused, Code::PermissionDenied);

    node.pin.replace(Some(
        PinnedPanelCertificate::from_der(node.panel.der.clone()).unwrap(),
    ));
    assert!(
        agent_answers(channel).await.unwrap(),
        "pinning the panel again admits the same connection again"
    );
}

#[tokio::test]
async fn the_unix_socket_keeps_working_beside_the_listener() {
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };

    let channel = unix_channel(&node.socket_path).await;

    assert!(agent_answers(channel).await.unwrap());
}

#[tokio::test]
async fn idle_connections_flooding_the_fleet_port_do_not_stop_the_unix_socket() {
    let Some(node) = start_node(true, FleetLimits::new(Duration::from_secs(30), 4)).await else {
        return;
    };
    let mut idle = Vec::new();
    for _ in 0..32 {
        idle.push(
            TcpStream::connect(node.fleet_address.unwrap())
                .await
                .unwrap(),
        );
    }

    let channel = unix_channel(&node.socket_path).await;
    let answered = tokio::time::timeout(PATIENCE, agent_answers(channel))
        .await
        .expect("the unix socket must answer while the fleet port is flooded");

    assert!(answered.unwrap());
    // The control that the flood was real: with only four handshake slots, most of the 32 were
    // closed by the cap rather than held open.
    let mut closed = 0_usize;
    for connection in &mut idle {
        let mut buffer = [0_u8; 1];
        if let Ok(Ok(0) | Err(_)) =
            tokio::time::timeout(Duration::from_millis(500), connection.read(&mut buffer)).await
        {
            closed += 1;
        }
    }
    assert!(
        closed >= 20,
        "the cap must have closed most of the flood, closed {closed}"
    );
}

#[tokio::test]
async fn a_refused_fleet_connection_is_logged_at_warn() {
    let log = CapturedLog::start();
    let Some(node) = start_node(true, FleetLimits::standard()).await else {
        return;
    };

    assert!(!served(&node, None, &[&TLS13]).await);

    let deadline = Instant::now() + PATIENCE;
    loop {
        let found = log
            .lines()
            .into_iter()
            .any(|line| line.contains("WARN") && line.contains("a fleet connection was refused"));
        if found {
            break;
        }
        assert!(
            Instant::now() < deadline,
            "no warn line for the refused connection; the log was:\n{}",
            log.lines().join("\n")
        );
        tokio::time::sleep(POLL_INTERVAL).await;
    }
}

#[tokio::test]
async fn a_daemon_started_without_the_fleet_flags_serves_the_unix_socket_and_has_no_fleet_port() {
    let Some(node) = start_node(false, FleetLimits::standard()).await else {
        return;
    };

    // `serve` was handed `None`, the shipped default: it builds no TLS configuration and binds
    // no TCP socket (`server.rs` only binds a `BoundFleetListener` it was given), so the unix
    // socket is the only way in. That nothing else listens is a property of that code path and
    // not something a probe of this process could show: the other tests in this file open
    // listeners of their own in the same process.
    assert!(node.fleet_address.is_none());
    assert!(
        agent_answers(unix_channel(&node.socket_path).await)
            .await
            .unwrap()
    );
}
