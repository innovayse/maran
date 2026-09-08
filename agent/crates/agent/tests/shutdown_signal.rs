//! What a real `maran-agent` process does when it is asked to stop.
//!
//! The whole subject of this suite is a signal delivered to a process, so it
//! spawns the SHIPPED BINARY and signals it. An in-process test cannot: a
//! `SIGTERM` raised inside the test harness kills the harness, and a test that
//! called the shutdown future directly would be observing its own argument.
//!
//! What was measured before the handler existed, and is the reason this suite
//! is here: the agent installed no handler at all, so `SIGTERM` took its
//! default action and ended the process in 3.5 ms, mid-syscall, exit 143, with
//! the socket file left behind. `systemctl stop` during an account creation
//! could therefore land between `useradd --create-home` and the step that opens
//! the home to the web server's group, leaving a user whose every site answers
//! 403 and which the agent deliberately refuses to repair.
//!
//! What it does NOT settle, stated so nobody reads more into a green run: the
//! drain BUDGET — that a request still running after `shutdown::DRAIN_BUDGET`
//! is abandoned rather than allowed to hold the daemon open — is asserted by
//! the unit tests on `drain_deadline`, on a paused clock. Proving it end to end
//! means holding a real `TailSiteLog` stream open for thirty seconds of wall
//! time, which is a suite nobody would run.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;
use std::process::{Child, Command};
use std::time::{Duration, Instant};

use hyper_util::rt::TokioIo;
use maran_agent::proto::GetAgentInfoRequest;
use maran_agent::proto::system_service_client::SystemServiceClient;
use tokio::net::UnixStream;
use tonic::transport::{Channel, Endpoint, Uri};
use tower::service_fn;

/// How long the test waits for the binary to bind before declaring it stuck.
const BIND_TIMEOUT: Duration = Duration::from_secs(10);

/// How long the stopped process is given to exit.
///
/// Far above the drain of an idle daemon (measured in milliseconds) and far
/// below the unit's `TimeoutStopSec=45`, so a failure here means the stop path
/// is broken rather than that the machine is loaded.
const EXIT_TIMEOUT: Duration = Duration::from_secs(10);

/// Gap between two polls. Polling and not sleeping out a fixed wait: the events
/// being waited for are a file appearing and a process reaping, and both are
/// observable directly (rules/testing.md "Determinism").
const POLL_INTERVAL: Duration = Duration::from_millis(20);

/// Authority the endpoint is built with; never resolved, because the connector
/// below dials the socket path instead. tonic still requires a valid URI to
/// build the `:authority` header from.
const UNUSED_AUTHORITY: &str = "http://uds.invalid";

#[test]
fn a_running_agent_exits_cleanly_on_sigterm_and_takes_its_socket_with_it() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let Some(mut agent) = start_agent(&socket_path) else {
        return;
    };

    let stopped_at = Instant::now();
    signal_terminate(&agent);
    let status = wait_for_exit(&mut agent);

    assert_eq!(
        status.code(),
        Some(0),
        "a stopped agent must EXIT, not be killed: an exit code of None means it \
         died by a signal, which is the no-handler behaviour this suite exists \
         to keep gone (it exited after {:?})",
        stopped_at.elapsed()
    );
    assert!(
        !socket_path.exists(),
        "a stopped agent must take its socket with it, so the panel cannot dial \
         a daemon that is gone: {} still exists",
        socket_path.display()
    );
}

/// The panel holds its connection to the agent open for the life of the panel
/// process, so the ordinary stop is a stop with a client attached. It must not
/// wait for that client to go away.
#[test]
fn a_connected_client_does_not_hold_the_stop_open() {
    let directory = tempfile::tempdir().unwrap();
    let socket_path = directory.path().join("agent.sock");

    let Some(mut agent) = start_agent(&socket_path) else {
        return;
    };

    // A real gRPC client, connected and having completed a call, held for the
    // whole of the stop — which is what the panel process is. A bare
    // `UnixStream` was tried here first and is NOT the same test: a peer that
    // never completes the HTTP/2 handshake has no connection hyper can send a
    // GOAWAY on, so it holds the drain open for the full budget. That is a real
    // property of the stop path (and the reason the budget is bounded at all),
    // but it is a property about a peer the panel never is, and asserting it
    // here would have made this suite thirty seconds long to prove something
    // about a caller that does not exist.
    let runtime = tokio::runtime::Runtime::new().expect("a runtime must be available");
    let _client = runtime.block_on(connected_client(&socket_path));

    signal_terminate(&agent);
    let status = wait_for_exit(&mut agent);

    assert_eq!(
        status.code(),
        Some(0),
        "an attached client must not turn a stop into a kill"
    );
}

/// Starts the shipped binary on `socket_path`, or answers `None` because this
/// host is outside the supported matrix.
///
/// `None` and not a panic, for the reason `handshake.rs` gives: detection runs
/// before the bind, so on an unsupported host there is no daemon to signal and
/// nothing this suite claims is even in question. Every OTHER failure to bind
/// is a panic, so the skip cannot swallow a broken stop path.
fn start_agent(socket_path: &Path) -> Option<Child> {
    let uid = maran_agent_core::utils::current_uid::current_uid().unwrap();

    let mut agent = Command::new(env!("CARGO_BIN_EXE_maran-agent"))
        .arg("--socket")
        .arg(socket_path)
        .arg("--allow-uid")
        .arg(uid.to_string())
        .spawn()
        .expect("the agent binary must be runnable");

    let deadline = Instant::now() + BIND_TIMEOUT;
    loop {
        if socket_path.exists() {
            return Some(agent);
        }

        if let Some(status) = agent.try_wait().expect("the child must be waitable") {
            assert_eq!(
                status.code(),
                Some(1),
                "the agent stopped before binding, and not with the startup \
                 failure code"
            );
            eprintln!("skipping: this host cannot run the agent (it refused to start)");
            return None;
        }

        assert!(
            Instant::now() < deadline,
            "the agent did not bind {} within {BIND_TIMEOUT:?}",
            socket_path.display()
        );
        std::thread::sleep(POLL_INTERVAL);
    }
}

/// Sends `SIGTERM` to the running agent — the signal systemd sends.
///
/// Through `kill(1)` rather than `libc::kill`, because `unsafe` in this
/// workspace lives in `maran-agent-core::privs` and nowhere else
/// (rules/rust.md), and an argv array is not a shell string.
fn signal_terminate(agent: &Child) {
    let killed = Command::new("kill")
        .args(["-TERM", &agent.id().to_string()])
        .status()
        .expect("kill(1) is present on every supported host");

    assert!(killed.success(), "the signal must be delivered");
}

/// Waits for the agent to be reaped, or fails after [`EXIT_TIMEOUT`].
///
/// The timeout is the assertion that matters as much as the exit code: a daemon
/// that never exits is exactly the outcome an unbounded drain would produce, and
/// a `wait()` with no deadline would hang the whole suite instead of naming it.
fn wait_for_exit(agent: &mut Child) -> std::process::ExitStatus {
    let deadline = Instant::now() + EXIT_TIMEOUT;

    loop {
        if let Some(status) = agent.try_wait().expect("the child must be waitable") {
            return status;
        }

        if Instant::now() >= deadline {
            let _ = agent.kill();
            let _ = agent.wait();
            panic!("the agent did not exit within {EXIT_TIMEOUT:?} of SIGTERM");
        }

        std::thread::sleep(POLL_INTERVAL);
    }
}

/// Dials the agent with the generated client and completes one call on it, so
/// what is held across the stop is an established HTTP/2 connection and not a
/// socket that never spoke.
async fn connected_client(socket_path: &Path) -> SystemServiceClient<Channel> {
    let path = socket_path.to_path_buf();

    let channel = Endpoint::try_from(UNUSED_AUTHORITY)
        .unwrap()
        .connect_with_connector(service_fn(move |_: Uri| {
            let path = path.clone();
            async move { Ok::<_, std::io::Error>(TokioIo::new(UnixStream::connect(path).await?)) }
        }))
        .await
        .expect("the agent's socket must accept the uid it was started for");

    let mut client = SystemServiceClient::new(channel);
    client
        .get_agent_info(GetAgentInfoRequest {})
        .await
        .expect("a running agent answers its own handshake");

    client
}
