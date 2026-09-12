//! The real vsftpd the FTPS suite logs in to, configured by the agent and
//! started by the test.
//!
//! **The configuration is the agent's own.** Every test that needs a running
//! daemon goes through [`PolygonVsftpd::apply`], which calls
//! `ops::ftps::enable_ftps` — so the file the daemon reads is the one
//! `safe_write` swapped into `/etc/maran/vsftpd/vsftpd.conf`, rendered from the
//! shipped template with THIS host's TLS key spellings. A fixture that wrote its
//! own `vsftpd.conf` would prove that the fixture works.
//!
//! **UNOBSERVED HERE: the service manager.** The polygon images install a shim
//! at `/usr/bin/systemctl` (see `docker/polygon/*.Dockerfile`) which records a
//! unit's state and starts NOTHING — a container has no init system. So the
//! restart `enable_ftps` performs, and the `is-active` it then asks, are
//! answered by a script rather than by systemd, and this suite proves nothing
//! about them. What it does instead is run the exact `ExecStart=` line
//! `installer/systemd/maran-ftps.service` declares,
//! `/usr/sbin/vsftpd /etc/maran/vsftpd/vsftpd.conf -obackground=NO`, so the
//! DAEMON and the CONFIGURATION under test are the real ones even though the
//! thing that would launch them on a server is not.
//!
//! One consequence is visible in [`PolygonVsftpd::apply`] and is not smoothed
//! over: the FIRST enable on a host with no daemon running gets
//! `FtpsError::NotListening` back, because the shim's restart brought nothing up
//! and the control port therefore answered nothing. `enable_ftps` leaves the
//! refused file on disk in that case, deliberately and by its own documented
//! behaviour, which is what this fixture then starts the daemon against.

use std::io::ErrorKind;
use std::net::TcpStream;
use std::path::Path;
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::web::domain::Domain;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::ftps::{FtpsConfiguration, FtpsError, FtpsState, ProcessFtpsHost, enable_ftps};
use maran_ops::sites::SiteCertificate;

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The daemon, at the path both families install it to and the unit names.
const VSFTPD_BINARY: &str = "/usr/sbin/vsftpd";

/// The one option the unit passes on the command line rather than in the file.
///
/// The two families disagree about the `background` default, and a daemon that
/// forks is one an init system immediately considers dead. Passed here for the
/// same reason the unit passes it: this fixture holds the process handle.
const FOREGROUND: &str = "-obackground=NO";

/// The control port the rendered configuration listens on.
const CONTROL_PORT: u16 = 21;

/// Where the daemon connects from, and the only address anything here touches.
const LOOPBACK: &str = "127.0.0.1";

/// vsftpd's own privilege-separation directory.
///
/// `RuntimeDirectory=` in the unit creates it, empty and root-owned, and takes
/// it away on stop. Nothing creates it in a container with no init system, so
/// the fixture does what the unit would: the daemon refuses to start without it.
const SECURE_CHROOT_DIR: &str = "/run/maran-ftps/empty";

/// Where the daemon writes its transfer log.
///
/// The rendered configuration names it, and vsftpd refuses to start if the
/// directory is missing. Created here for the same reason as the directory
/// above: the installer makes it on a real host.
const LOG_DIRECTORY: &str = "/var/log/maran";

/// How long the fixture waits for the daemon to accept a connection.
const START_TIMEOUT: Duration = Duration::from_secs(20);

/// Gap between two connection attempts while the daemon starts.
const START_POLL_INTERVAL: Duration = Duration::from_millis(100);

/// A running vsftpd on the polygon host, and the configuration it was started
/// against.
///
/// Stopped in [`Drop`], including on a panic, so one test's daemon is never what
/// a later test is really talking to.
pub struct PolygonVsftpd {
    /// The daemon process, while it is running.
    daemon: Option<Child>,
}

impl PolygonVsftpd {
    /// Refuses to go on unless this process is root inside a polygon image.
    ///
    /// A panic and not a quiet `return`, for the reason every polygon fixture
    /// panics: these suites are `#[ignore]`d, so a skip would report as a pass
    /// and a suite that never spoke to a vsftpd would count as coverage of one
    /// (rules/testing.md).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent or the process is not root.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests create real system logins, mount real filesystems, start a \
             real vsftpd and log in to it, and must run only inside a polygon \
             container: {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "creating a login and starting a daemon both require root"
        );
    }

    /// Applies `configuration` through `enable_ftps` and starts the daemon on
    /// the file it left behind.
    ///
    /// The enable's own outcome is returned rather than swallowed, so a test
    /// that cares about it can assert on it. It is not asserted here because
    /// both outcomes are legitimate in a container: `NotListening` the first
    /// time, when nothing was up for the control-port probe to reach, and `Ok`
    /// afterwards, when the previous daemon was still answering while the new
    /// file was swapped in.
    ///
    /// # Panics
    ///
    /// Panics when the daemon cannot be started or does not accept a connection
    /// within [`START_TIMEOUT`].
    pub fn apply(configuration: &FtpsConfiguration) -> (Self, Result<FtpsState, FtpsError>) {
        Self::require_polygon();
        Self::prepare_runtime_directories();

        let outcome = enable_ftps(&ProcessFtpsHost::new(), Self::distro(), configuration);
        let daemon = Self::start();

        (daemon, outcome)
    }

    /// Applies `configuration` to a host whose daemon is ALREADY running, and
    /// restarts it onto the result.
    ///
    /// Separate from [`Self::apply`] because the enable path's last step is to
    /// ask the control port for a greeting and to ROLL BACK when it gets none:
    /// with the daemon stopped first, an otherwise perfect enable returns
    /// `NotListening` and puts the previous file back. So a test that is
    /// re-applying — which is what the panel does, and what the repair after a
    /// sabotaged configuration is — leaves the old daemon answering while the
    /// new file is swapped in, exactly as a real host does.
    ///
    /// # Panics
    ///
    /// Panics for the reasons [`Self::start`] does.
    pub fn reapply(&mut self, configuration: &FtpsConfiguration) -> Result<FtpsState, FtpsError> {
        let outcome = enable_ftps(&ProcessFtpsHost::new(), Self::distro(), configuration);
        self.restart();
        outcome
    }

    /// Starts the daemon on whatever configuration is currently on disk.
    ///
    /// # Panics
    ///
    /// Panics when the binary cannot be run, or when nothing is accepting
    /// connections on the control port within [`START_TIMEOUT`].
    pub fn start() -> Self {
        Self::require_polygon();
        Self::prepare_runtime_directories();

        let daemon = Command::new(VSFTPD_BINARY)
            .arg(AgentPaths::VSFTPD_CONFIG_PATH)
            .arg(FOREGROUND)
            .stdout(Stdio::null())
            .stderr(Stdio::inherit())
            .spawn()
            .unwrap_or_else(|error| panic!("the polygon image installs {VSFTPD_BINARY}: {error}"));

        let mut started = Self {
            daemon: Some(daemon),
        };
        started.wait_until_answering();
        started
    }

    /// Stops the daemon, restarts it on the file that is on disk NOW, and waits
    /// for it.
    ///
    /// The one way a test observes a config edit taking effect: vsftpd reads its
    /// configuration once, at start.
    ///
    /// # Panics
    ///
    /// Panics for the reasons [`Self::start`] does.
    pub fn restart(&mut self) {
        self.stop();
        *self = Self::start();
    }

    /// Stops the daemon if it is running, and waits for it to be gone.
    pub fn stop(&mut self) {
        if let Some(mut daemon) = self.daemon.take() {
            let _ = daemon.kill();
            let _ = daemon.wait();
        }

        // The control port has to be free before the next daemon binds it, and a
        // killed process releases its socket when the kernel reaps it rather
        // than when `wait` returns. Polled, never slept on (rules/testing.md).
        let deadline = Instant::now() + START_TIMEOUT;
        while Instant::now() < deadline && Self::control_port_accepts() {
            std::thread::sleep(START_POLL_INTERVAL);
        }
    }

    /// Blocks until something accepts on the control port, or fails the test.
    ///
    /// # Panics
    ///
    /// Panics when nothing is listening within [`START_TIMEOUT`], naming the
    /// daemon's own exit status if it has one — a vsftpd that refused its
    /// configuration exits, and its message is on this process's stderr.
    fn wait_until_answering(&mut self) {
        let deadline = Instant::now() + START_TIMEOUT;

        while Instant::now() < deadline {
            if Self::control_port_accepts() {
                return;
            }

            // A vsftpd that refused its configuration EXITS, and its message —
            // when it has one; the Debian family's build prints nothing at all
            // for an unrecognised variable — went to this process's stderr. The
            // status is what says which of the two happened, and waiting the
            // whole timeout out on a process that is already gone would hide it.
            if let Some(daemon) = self.daemon.as_mut()
                && let Ok(Some(status)) = daemon.try_wait()
            {
                panic!(
                    "vsftpd exited with {status} instead of listening on port \
                     {CONTROL_PORT}; anything it printed is on stderr above, and \
                     printing nothing at all is what the Debian family's build \
                     does for a key it does not know"
                );
            }

            std::thread::sleep(START_POLL_INTERVAL);
        }

        panic!(
            "vsftpd did not accept a connection on port {CONTROL_PORT} within \
             {START_TIMEOUT:?} and did not exit either"
        );
    }

    /// Whether a TCP connect to the control port is accepted right now.
    fn control_port_accepts() -> bool {
        match TcpStream::connect((LOOPBACK, CONTROL_PORT)) {
            Ok(_) => true,
            Err(error) => !matches!(
                error.kind(),
                ErrorKind::ConnectionRefused | ErrorKind::TimedOut
            ),
        }
    }

    /// Creates the two directories the unit and the installer would.
    ///
    /// Not part of what is under test: `RuntimeDirectory=` in
    /// `installer/systemd/maran-ftps.service` makes the first on a real host and
    /// `installer/lib/89-ftps.sh` makes the second, and a container has neither.
    /// Without them vsftpd refuses to start at all, which would fail every test
    /// in the suite for a reason that has nothing to do with the agent.
    fn prepare_runtime_directories() {
        for directory in [SECURE_CHROOT_DIR, LOG_DIRECTORY] {
            std::fs::create_dir_all(directory)
                .unwrap_or_else(|error| panic!("the fixture must create {directory}: {error}"));
        }
    }
}

impl Drop for PolygonVsftpd {
    /// Stops the daemon whether the test passed or panicked.
    fn drop(&mut self) {
        self.stop();
    }
}

/// Everything the suite needs from the polygon that is not the daemon itself.
///
/// Associated functions rather than free ones because a fixture file holds one
/// public unit like every other file in this repository (rules/rust.md), and the
/// unit here is the polygon's FTPS environment: the adapter its family answers
/// with, and the certificate material an enable has to be given.
impl PolygonVsftpd {
    /// The distribution adapter for the polygon this suite is running in.
    ///
    /// # Panics
    ///
    /// Panics when the host is outside the support matrix, which a polygon image
    /// never is.
    pub fn distro() -> &'static dyn DistroAdapter {
        adapter_for(
            detect()
                .expect("a polygon image is a supported host")
                .family,
        )
    }

    /// Places real certificate material for `domain` where the agent's store keeps
    /// it, so an enable has something to serve.
    ///
    /// **Written with the host's own `openssl` at the store's own paths, and not
    /// through `ops::ssl::install_certificate`.** That operation re-renders the
    /// site's nginx vhost as part of installing material, so it needs a site and a
    /// running nginx — neither of which this suite is about, and both of which are
    /// exercised in `sites_on_a_real_host.rs`. What the FTPS enable does with the
    /// store is READ it: two files present at the derived paths. Real material, at
    /// the real path, is exactly what that read has to be given.
    ///
    /// # Panics
    ///
    /// Panics when the directory cannot be made or `openssl` refuses.
    pub fn place_certificate_material(domain: &Domain) {
        let certificate = SiteCertificate::for_domain(domain);
        let directory = certificate
            .certificate_path()
            .parent()
            .expect("the store's paths always have a parent");
        std::fs::create_dir_all(directory)
            .unwrap_or_else(|error| panic!("the certificate directory must be creatable: {error}"));

        if certificate.certificate_path().exists() && certificate.key_path().exists() {
            return;
        }

        let outcome = Command::new(Self::distro().openssl_binary())
            .args([
                "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2", "-subj",
            ])
            .arg(format!("/CN={}", domain.as_str()))
            .arg("-keyout")
            .arg(certificate.key_path())
            .arg("-out")
            .arg(certificate.certificate_path())
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs openssl: {error}"));

        assert!(
            outcome.status.success(),
            "openssl must produce material for {}: {}",
            domain.as_str(),
            String::from_utf8_lossy(&outcome.stderr)
        );
    }

    /// Removes the material [`place_certificate_material`] wrote.
    pub fn remove_certificate_material(domain: &Domain) {
        let certificate = SiteCertificate::for_domain(domain);
        for path in [certificate.certificate_path(), certificate.key_path()] {
            Self::remove_if_present(path);
        }
    }

    /// Removes `path` if it is there, reporting rather than panicking.
    fn remove_if_present(path: &Path) {
        if path.exists()
            && let Err(error) = std::fs::remove_file(path)
        {
            eprintln!("the polygon file {path:?} could not be removed: {error}");
        }
    }
}
