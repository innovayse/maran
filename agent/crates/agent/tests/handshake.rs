//! The handshake over a real unix socket: a server started in-process is
//! answered by the generated client, so the proto contract, the codec and the
//! transport are all exercised rather than mocked.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::time::{Duration, Instant};

use hyper_util::rt::TokioIo;
use tokio::net::UnixStream;
use tonic::transport::{Channel, Endpoint, Uri};
use tower::service_fn;

use maran_agent::error::StartupError;
use maran_agent::peercred::PeerPolicy;
use maran_agent::proto::system_service_client::SystemServiceClient;
use maran_agent::proto::{GetAgentInfoRequest, get_agent_info_response};

/// How long the test waits for the server to bind before declaring it stuck.
const BIND_TIMEOUT: Duration = Duration::from_secs(5);

/// Gap between two checks for the socket file while the server starts.
const BIND_POLL_INTERVAL: Duration = Duration::from_millis(20);

/// Authority the endpoint is built with. It is never resolved: the custom
/// connector below ignores it and dials the socket path instead, but tonic still
/// requires a syntactically valid URI to build the `:authority` header from.
const UNUSED_AUTHORITY: &str = "http://uds.invalid";

/// What happened while the server was starting.
enum Started {
    /// The socket exists and is ready to be dialled.
    Listening,
    /// The host is outside the supported matrix, so there is nothing to test.
    UnsupportedHost(String),
}

#[tokio::test]
async fn handshake_over_a_unix_socket_reports_the_agent_and_the_host() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let policy = PeerPolicy::new(maran_agent::config::current_uid::current_uid().unwrap());
    let server_path = socket_path.clone();
    let mut server =
        tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

    match wait_until_listening(&socket_path, &mut server).await {
        Started::Listening => {}
        Started::UnsupportedHost(reason) => {
            eprintln!("skipping the handshake test: {reason}");
            return;
        }
    }

    let mut client = SystemServiceClient::new(connect(&socket_path).await);
    let response = client
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .unwrap()
        .into_inner();

    let info = match response.result {
        Some(get_agent_info_response::Result::Ok(info)) => info,
        other => panic!("the handshake must succeed on a supported host, got {other:?}"),
    };

    assert_eq!(info.proto_version, 1);
    assert!(
        !info.version.is_empty(),
        "the agent must report its version"
    );
    assert_eq!(info.distro_id, host_distro_id());

    server.abort();
}

/// Polls for the socket file until the server binds it, the server gives up, or
/// [`BIND_TIMEOUT`] elapses.
///
/// The socket file appearing is the only observable "ready" signal a bound
/// listener leaves behind, so it is polled rather than waited out with a fixed
/// sleep that would be both slower and racier.
async fn wait_until_listening(
    socket_path: &Path,
    server: &mut tokio::task::JoinHandle<Result<(), StartupError>>,
) -> Started {
    let deadline = Instant::now() + BIND_TIMEOUT;

    loop {
        if socket_path.exists() {
            return Started::Listening;
        }

        if server.is_finished() {
            let outcome = server.await.expect("the server task must not panic");
            return match outcome {
                Err(StartupError::Distro(error)) => Started::UnsupportedHost(error.to_string()),
                other => panic!("the server stopped before binding: {other:?}"),
            };
        }

        assert!(
            Instant::now() < deadline,
            "the server did not bind {} within {BIND_TIMEOUT:?}",
            socket_path.display()
        );

        tokio::time::sleep(BIND_POLL_INTERVAL).await;
    }
}

/// Opens a gRPC channel onto the unix socket at `socket_path`.
async fn connect(socket_path: &Path) -> Channel {
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

/// The host's os-release `ID`, read independently of the agent's own detection
/// so the assertion checks the reported value against the system rather than
/// against the code under test.
fn host_distro_id() -> String {
    let content = std::fs::read_to_string("/etc/os-release").unwrap();

    content
        .lines()
        .find_map(|line| line.strip_prefix("ID="))
        .map(|value| value.trim_matches('"').to_owned())
        .expect("a Linux host publishes ID in /etc/os-release")
}
