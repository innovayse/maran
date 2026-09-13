//! The real MariaDB the database suite runs against, started by the test.
//!
//! The polygon images install the server from the installer's own package list
//! and leave it stopped — an image that shipped a running daemon would be a
//! container pretending to be a host. Starting it is therefore the suite's job,
//! and this is where it happens: once per test process, whatever order the
//! tests run in.

use std::process::{Command, Output, Stdio};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

use maran_distro::{DistroAdapter, adapter_for, detect};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The wrapper that starts the server and keeps its data directory in order.
///
/// Spelled by name rather than by absolute path: it is the test harness
/// starting a daemon, not the agent running a tool, so nothing here is part of
/// the surface `rules/rust.md` requires an allow-listed absolute path for. The
/// AGENT's own client path still comes from the adapter, below, because that is
/// the path the code under test will execute.
const SERVER_WRAPPER: &str = "mariadbd-safe";

/// How long the fixture waits for the server to accept a connection.
const START_TIMEOUT: Duration = Duration::from_secs(60);

/// Gap between two connection attempts while the server starts.
const START_POLL_INTERVAL: Duration = Duration::from_millis(250);

/// Whether this process has already started the server.
static STARTED: OnceLock<()> = OnceLock::new();

/// A running MariaDB on the polygon host, reachable exactly as the agent
/// reaches it: over the local socket, as root, with no credential at all.
///
/// The fixture never supplies a password and never reads one from anywhere. If
/// the server on this host wanted one, every method here would fail — which is
/// the correct outcome, because `ProcessDbHost` would fail in exactly the same
/// way, and a suite that worked around it would be testing a configuration the
/// agent cannot use.
pub struct PolygonMariadb {
    /// The client binary, taken from the distro adapter — the same path the
    /// agent will execute, so a family whose adapter names the wrong one fails
    /// here rather than on a customer's server.
    client: &'static str,
}

impl PolygonMariadb {
    /// Refuses to go on unless this process is root inside a polygon image.
    ///
    /// A panic and not a quiet `return`. These suites are `#[ignore]`d, so the
    /// only way to reach them is to ask for them by name — and a skip would
    /// then report as a pass, which is how a suite that never spoke to a
    /// database comes to be counted as coverage of one (rules/testing.md: "no
    /// tests found" is a failure, never a pass).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent or the process is not root.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests create real databases and real database users on a real \
             server, and must run only inside a polygon container: \
             {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "the agent authenticates to MariaDB by the uid on the socket, so this \
             suite has to be the uid the server knows as root"
        );
    }

    /// Starts the server if this process has not already, and waits for it.
    ///
    /// # Panics
    ///
    /// Panics outside a polygon, when the server cannot be started, or when it
    /// does not accept a connection within [`START_TIMEOUT`].
    pub fn start() -> Self {
        Self::require_polygon();

        let fixture = Self {
            client: Self::distro().mysql_client_binary(),
        };

        STARTED.get_or_init(|| {
            if fixture.can_connect() {
                return;
            }

            // Deliberately never waited for: the server has to outlive this
            // call and every test after it, which is the opposite of what the
            // lint is usually protecting against. The container is torn down
            // when the run ends, so there is nothing to reap and nowhere for a
            // zombie to accumulate.
            #[allow(clippy::zombie_processes)]
            Command::new(SERVER_WRAPPER)
                .args(["--skip-networking", "--skip-syslog"])
                .stdin(Stdio::null())
                .stdout(Stdio::null())
                .stderr(Stdio::null())
                .spawn()
                .unwrap_or_else(|error| {
                    panic!("the polygon image installs {SERVER_WRAPPER}: {error}")
                });

            let deadline = Instant::now() + START_TIMEOUT;
            while !fixture.can_connect() {
                assert!(
                    Instant::now() < deadline,
                    "MariaDB did not accept a socket connection from root within \
                     {START_TIMEOUT:?}. The agent holds no credential, so this is \
                     either a server that did not start or one whose root@localhost \
                     is not on the unix socket plugin."
                );
                std::thread::sleep(START_POLL_INTERVAL);
            }
        });

        fixture
    }

    /// Runs one statement as root over the socket and returns the whole output.
    ///
    /// Deliberately NOT the agent's `ProcessDbHost`: this is the independent
    /// observer the suite checks the agent's work with, so it spawns the client
    /// itself. A test that asked the code under test what the code under test
    /// had done would pass on a `create_database` that did nothing.
    ///
    /// # Panics
    ///
    /// Panics when the client cannot be run at all.
    pub fn run(&self, statement: &str) -> Output {
        Command::new(self.client)
            .args(["--batch", "--skip-column-names", "--execute", statement])
            .stdin(Stdio::null())
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs {}: {error}", self.client))
    }

    /// Runs one statement as `user`, authenticating with `password`.
    ///
    /// The credential is passed on the command line, which is exactly what the
    /// agent must never do and is fine here: this is the test acting as the
    /// CUSTOMER's application would, the password is one the test just minted,
    /// and the container is thrown away at the end of the run.
    ///
    /// # Panics
    ///
    /// Panics when the client cannot be run at all.
    pub fn run_as(&self, user: &str, password: &str, statement: &str) -> Output {
        Command::new(self.client)
            .args([
                "--protocol=socket",
                &format!("--user={user}"),
                &format!("--password={password}"),
                "--batch",
                "--skip-column-names",
                "--execute",
                statement,
            ])
            .stdin(Stdio::null())
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs {}: {error}", self.client))
    }

    /// Whether root can reach the server over the socket with no credential.
    fn can_connect(&self) -> bool {
        self.run("SELECT 1").status.success()
    }

    /// The distribution adapter for the polygon this suite is running in.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix, which a polygon
    /// image never is.
    fn distro() -> &'static dyn DistroAdapter {
        adapter_for(
            detect()
                .expect("a polygon image is a supported host")
                .family,
        )
    }
}
