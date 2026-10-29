//! A real agent server started in this process, on a socket of its own, and a
//! client channel already connected to it.
//!
//! The server is `maran_agent::server::serve`, the function the daemon's `main`
//! calls, with a policy that admits the uid the test runs as: so a request sent
//! through [`InProcessAgent::channel`] travels the real codec, the real peer
//! check, the real handler and the real error mapping. Nothing is stubbed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::time::{Duration, Instant};

use hyper_util::rt::TokioIo;
use tokio::net::UnixStream;
use tonic::transport::{Channel, Endpoint, Uri};
use tower::service_fn;

use maran_agent::error::StartupError;
use maran_agent::peercred::PeerPolicy;

/// How long the server is given to bind its socket.
const BIND_TIMEOUT: Duration = Duration::from_secs(10);

/// Gap between two looks for the socket file while the server starts.
const BIND_POLL_INTERVAL: Duration = Duration::from_millis(20);

/// Authority the endpoint is built with. Never resolved: the connector below
/// dials the socket path, but tonic still needs a well-formed URI.
const UNUSED_AUTHORITY: &str = "http://uds.invalid";

/// A running in-process agent and a channel connected to it.
///
/// Dropping it stops the server task. The socket lives in a temporary
/// directory that is removed with it.
pub struct InProcessAgent {
    /// The client channel, connected to the server's socket.
    channel: Channel,
    /// The server task, aborted on drop.
    server: tokio::task::JoinHandle<Result<(), StartupError>>,
    /// Owns the directory holding the socket; kept so it outlives the server.
    _directory: tempfile::TempDir,
}

impl InProcessAgent {
    /// Starts the server and connects a channel to it.
    ///
    /// # Panics
    ///
    /// Panics when the server stops before binding or does not bind within
    /// [`BIND_TIMEOUT`], or when the channel cannot connect. A failing
    /// assertion is the reporting mechanism of a test.
    pub async fn start() -> Self {
        let directory = tempfile::tempdir().expect("a temporary directory for the socket");
        let socket_path = directory.path().join("agent.sock");

        let uid = maran_agent_core::utils::current_uid::current_uid()
            .expect("the uid of this process is readable");
        let policy = PeerPolicy::new(uid);
        let server_path = socket_path.clone();
        let server =
            tokio::spawn(async move { maran_agent::server::serve(&server_path, policy).await });

        wait_until_listening(&socket_path, &server).await;
        let channel = connect(&socket_path).await;

        Self {
            channel,
            server,
            _directory: directory,
        }
    }

    /// A clone of the connected channel, for a generated service client.
    pub fn channel(&self) -> Channel {
        self.channel.clone()
    }
}

impl Drop for InProcessAgent {
    /// Stops the server task.
    fn drop(&mut self) {
        self.server.abort();
    }
}

/// Polls for the socket file until it appears, the server gives up, or
/// [`BIND_TIMEOUT`] elapses.
///
/// # Panics
///
/// Panics when the server task finished first or the budget ran out.
async fn wait_until_listening(
    socket_path: &Path,
    server: &tokio::task::JoinHandle<Result<(), StartupError>>,
) {
    let deadline = Instant::now() + BIND_TIMEOUT;

    loop {
        if socket_path.exists() {
            return;
        }

        assert!(
            !server.is_finished(),
            "the in-process agent stopped before it bound {}",
            socket_path.display()
        );
        assert!(
            Instant::now() < deadline,
            "the in-process agent did not bind {} within {BIND_TIMEOUT:?}",
            socket_path.display()
        );

        tokio::time::sleep(BIND_POLL_INTERVAL).await;
    }
}

/// Opens a gRPC channel onto the unix socket at `socket_path`.
///
/// # Panics
///
/// Panics when the connection cannot be made.
async fn connect(socket_path: &Path) -> Channel {
    let path = socket_path.to_path_buf();

    Endpoint::try_from(UNUSED_AUTHORITY)
        .expect("a well-formed authority")
        .connect_with_connector(service_fn(move |_: Uri| {
            let path = path.clone();
            async move { Ok::<_, std::io::Error>(TokioIo::new(UnixStream::connect(path).await?)) }
        }))
        .await
        .expect("the channel connects to the socket")
}
