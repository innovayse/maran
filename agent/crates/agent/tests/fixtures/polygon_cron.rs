//! The real cron daemon the cron suite's entries are executed by.
//!
//! A container has no init system, so nothing starts cron. This fixture starts
//! the family's own daemon as a real foreground process and kills it when the
//! test ends — the same shape `PolygonSshd` uses, and for the same reason: the
//! proposition under test is that a REAL cron accepts and runs what the agent
//! installed, and a stand-in for the daemon would make that proposition
//! circular.
//!
//! The daemon is the one thing here the `DistroAdapter` cannot name. It answers
//! `cron_service()` — the UNIT name — because that is what the agent needs; the
//! executable behind it is a fact only a test that starts one has ever needed,
//! so the mapping lives here rather than widening the trait for a test.

use std::path::Path;
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

use maran_distro::{DistroFamily, detect};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The Debian family's cron daemon, and its stay-in-foreground flag.
const DEBIAN_CRON: (&str, &str) = ("/usr/sbin/cron", "-f");

/// The RHEL family's cron daemon, and its do-not-daemonise flag.
///
/// A different binary AND a different flag from the Debian family's: `crond`
/// spells "stay in the foreground" as `-n`. Getting either wrong produces a
/// fixture that returns a dead child and a suite that waits out its whole
/// deadline for an entry nothing was ever going to run, so both are asserted
/// against the filesystem before the child is spawned.
const RHEL_CRON: (&str, &str) = ("/usr/sbin/crond", "-n");

/// How long a test waits for cron to reach a `* * * * *` entry.
///
/// Cron wakes on the minute boundary, so the true worst case is just under 60
/// seconds plus the command's own run. The margin above that is for a loaded
/// runner, and it is a DEADLINE rather than a sleep: the tests poll for the
/// file they expect and stop the moment it is there (rules/testing.md
/// "Determinism").
pub const CRON_TICK_DEADLINE: Duration = Duration::from_secs(90);

/// Gap between two looks at the filesystem while waiting for a tick.
const POLL_INTERVAL: Duration = Duration::from_millis(500);

/// How long the fixture lets the daemon run before deciding it started.
///
/// Long enough for a refusal — a stale pid file, a spool it will not read — to
/// have happened, and short enough that it costs nothing when the daemon is
/// fine.
const START_GRACE: Duration = Duration::from_millis(500);

/// Where the daemon's two streams are captured.
///
/// Under /tmp rather than in the account's home: the home is removed when the
/// account fixture drops, and the log is most wanted when a test is failing.
const DAEMON_LOG: &str = "/tmp/maran-polygon-cron.log";

/// A running cron daemon on the polygon host.
pub struct PolygonCron {
    /// The daemon process, killed when this value is dropped.
    daemon: Child,
}

impl PolygonCron {
    /// Refuses to go on unless this process is root inside a polygon image.
    ///
    /// A panic and not a quiet `return`, for the reason every polygon fixture
    /// panics: these suites are `#[ignore]`d, so a skip would report as a pass
    /// and a suite that never installed a crontab would count as coverage of
    /// one (rules/testing.md).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent or the process is not root.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests install real crontabs and let a real cron daemon run \
             them, and must run only inside a polygon container: \
             {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "installing another account's crontab requires root"
        );
    }

    /// Starts the family's cron daemon in the foreground.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix, when the daemon this
    /// family installs is not where it should be, or when it cannot be started.
    pub fn start() -> Self {
        Self::require_polygon();

        let family = detect()
            .expect("a polygon image is a supported host")
            .family;
        let (binary, foreground) = match family {
            DistroFamily::Debian => DEBIAN_CRON,
            DistroFamily::Rhel => RHEL_CRON,
        };

        // Asserted before the spawn rather than discovered as a silent
        // non-start: a missing daemon would otherwise show up as every timed
        // test waiting out its full deadline, which reads like a broken agent.
        assert!(
            Path::new(binary).exists(),
            "the polygon image must install {family:?}'s cron daemon at {binary}"
        );

        // Both streams go to a file rather than to /dev/null, because a daemon
        // that refuses to start says so on one of them and then a timed test
        // waits out its whole deadline for an entry nothing was ever going to
        // run — a failure that reads like a broken agent and is not one.
        let log = std::fs::File::create(DAEMON_LOG)
            .unwrap_or_else(|error| panic!("the daemon's log must be writable: {error}"));
        let errors = log
            .try_clone()
            .unwrap_or_else(|error| panic!("the daemon's log must be shareable: {error}"));

        let mut daemon = Command::new(binary)
            .arg(foreground)
            .stdin(Stdio::null())
            .stdout(Stdio::from(log))
            .stderr(Stdio::from(errors))
            .spawn()
            .unwrap_or_else(|error| panic!("the cron daemon must start: {error}"));

        // Alive a moment later, not merely spawned. `spawn` succeeds for a
        // daemon that then exits immediately — a stale pid file, a spool it
        // will not read, a second instance — and every one of those looks
        // exactly like "the agent installed a table cron ignored".
        std::thread::sleep(START_GRACE);
        if let Ok(Some(status)) = daemon.try_wait() {
            panic!(
                "the cron daemon exited immediately with {status}; it said:\n{}",
                Self::log()
            );
        }

        Self { daemon }
    }

    /// Everything the daemon has written to either stream.
    ///
    /// For a failure message: "nothing ran" and "the daemon refused to start"
    /// are the same observation from the filesystem and call for opposite
    /// answers.
    pub fn log() -> String {
        std::fs::read_to_string(DAEMON_LOG).unwrap_or_else(|error| {
            format!("(the daemon's log at {DAEMON_LOG} could not be read: {error})")
        })
    }

    /// Waits until `path` exists, or gives up at [`CRON_TICK_DEADLINE`].
    ///
    /// Returns whether it appeared, so a caller can assert either way: one test
    /// here waits for a file that MUST appear and, in the same run, checks that
    /// a second file did not.
    pub fn wait_for(path: &Path) -> bool {
        let deadline = Instant::now() + CRON_TICK_DEADLINE;

        while Instant::now() < deadline {
            if path.exists() {
                return true;
            }

            std::thread::sleep(POLL_INTERVAL);
        }

        path.exists()
    }
}

impl Drop for PolygonCron {
    /// Stops the daemon, whether the test passed or panicked.
    fn drop(&mut self) {
        // A failure here cannot fail the test — a panic in `drop` during
        // another panic aborts the process and hides the real failure — so it
        // is reported and nothing more.
        if let Err(error) = self.daemon.kill() {
            eprintln!("the polygon cron daemon could not be stopped: {error}");
        }
        if let Err(error) = self.daemon.wait() {
            eprintln!("the polygon cron daemon could not be collected: {error}");
        }
    }
}
