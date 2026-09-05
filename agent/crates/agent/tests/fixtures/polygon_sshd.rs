//! The real OpenSSH daemon the SFTP suite logs in to, started by the test.
//!
//! The daemon reads `/etc/ssh/sshd_config` — the file the INSTALLER's own
//! `86-sftp.sh` appended its `Match Group` block to when the polygon image was
//! built. Nothing here writes a line of ssh configuration: a suite that
//! configured the daemon it then logged in to would prove that the suite works.
//!
//! It listens on a port of the suite's own rather than on 22, so the daemon
//! under test is unambiguously the one this fixture started.
//!
//! **Two daemon options are overridden, and only two.** The second,
//! `PerSourcePenalties=no`, is set only where the daemon knows it and is
//! explained at [`PolygonSshd::supports_per_source_penalties`]; it changes
//! nothing about whether a credential is checked, only whether the daemon
//! sulks at loopback after a refusal this suite asked for. The first is
//! `UsePAM=no`, and it does change what is checked. It is stated
//! here rather than buried, because it is the single thing this suite does not
//! run the way a real host runs it. The reason is a conflict inside the
//! container and not a preference: the suite needs a real bind mount, a
//! container can only make one when its AppArmor confinement is lifted, and on
//! the RHEL family a container with AppArmor lifted has a `pam_unix` that
//! refuses every account — key-based logins included, in the *account* phase,
//! long after the signature has verified. Measured on both families; the Debian
//! family is unaffected either way, and the option is set on both so the two
//! suites are the same suite.
//!
//! What that costs, precisely: with `UsePAM=no` sshd checks the password
//! against `/etc/shadow` itself instead of asking PAM. **The credential is still
//! really checked** — it is the hash `chpasswd` wrote, and a wrong password is
//! still refused, which this suite asserts — so what the agent set is still what
//! authenticates. What is NOT exercised is the host's PAM stack: its account
//! phase, its session modules, and any complexity policy an operator has
//! configured. Nothing else changes: the `Match Group` block, `ChrootDirectory`,
//! `ForceCommand` and every refusal this suite asserts are the daemon's own,
//! read from the installer's file.

use std::io::Write as _;
use std::os::unix::fs::PermissionsExt as _;
use std::path::PathBuf;
use std::process::{Command, Output, Stdio};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The daemon, at the path both families install it to.
const SSHD_BINARY: &str = "/usr/sbin/sshd";

/// The port the suite's own daemon listens on.
const SSHD_PORT: &str = "22022";

/// The address the suite connects to. Loopback only: nothing here should be
/// reachable from outside the container even by accident.
const SSHD_ADDRESS: &str = "127.0.0.1";

/// How long the fixture waits for the daemon to accept a connection.
const START_TIMEOUT: Duration = Duration::from_secs(20);

/// Gap between two connection attempts while the daemon starts.
const START_POLL_INTERVAL: Duration = Duration::from_millis(100);

/// Whether this process has already started the daemon.
static STARTED: OnceLock<()> = OnceLock::new();

/// A running OpenSSH daemon on the polygon host, and the two clients the suite
/// reaches it with.
///
/// Password authentication is used throughout, which is the point rather than a
/// convenience: it is the only way to find out whether the password the agent
/// handed `chpasswd` is the password the host now accepts. A key-based fixture
/// would prove the jail and prove nothing about the credential.
pub struct PolygonSshd;

impl PolygonSshd {
    /// Refuses to go on unless this process is root inside a polygon image.
    ///
    /// A panic and not a quiet `return`, for the reason every polygon fixture
    /// panics: these suites are `#[ignore]`d, so a skip would report as a pass
    /// and a suite that never spoke to an sshd would count as coverage of one
    /// (rules/testing.md).
    ///
    /// # Panics
    ///
    /// Panics when the polygon marker is absent or the process is not root.
    pub fn require_polygon() {
        let marker = std::env::var(POLYGON_MARKER).unwrap_or_default();
        assert!(
            !marker.is_empty(),
            "these tests create real system logins, mount real filesystems and log \
             in to a real sshd, and must run only inside a polygon container: \
             {POLYGON_MARKER} is not set. See docker/README.md."
        );
        assert_eq!(
            rustix::process::getuid().as_raw(),
            0,
            "creating a login and starting a daemon both require root"
        );
    }

    /// Starts the daemon if this process has not already, and waits for it.
    ///
    /// # Panics
    ///
    /// Panics outside a polygon, when the daemon refuses the host's own
    /// configuration, or when it is not accepting connections within
    /// [`START_TIMEOUT`].
    pub fn start() -> Self {
        Self::require_polygon();

        STARTED.get_or_init(|| {
            // The daemon's own opinion of the config the installer wrote, taken
            // BEFORE starting it so a rejected config is reported as itself
            // rather than as a daemon that would not come up.
            let checked = Command::new(SSHD_BINARY)
                .arg("-t")
                .output()
                .unwrap_or_else(|error| {
                    panic!("the polygon image installs {SSHD_BINARY}: {error}")
                });
            assert!(
                checked.status.success(),
                "sshd rejects the configuration the installer produced:\n{}",
                String::from_utf8_lossy(&checked.stderr)
            );

            // `UsePAM=no` is the suite's one override of the installer's
            // configuration that changes what is CHECKED, and the module comment
            // above says exactly what it costs and why the alternative is not
            // available inside a container. Everything else — the Match block,
            // the chroot, the forced command — comes from the file the
            // installer wrote.
            let mut options = vec!["-p", SSHD_PORT, "-o", "UsePAM=no"];
            if Self::supports_per_source_penalties() {
                options.extend_from_slice(&["-o", "PerSourcePenalties=no"]);
            }

            let started = Command::new(SSHD_BINARY)
                .args(&options)
                .stdin(Stdio::null())
                .output()
                .unwrap_or_else(|error| panic!("sshd could not be started: {error}"));
            assert!(
                started.status.success(),
                "sshd did not start on port {SSHD_PORT}:\n{}",
                String::from_utf8_lossy(&started.stderr)
            );

            let deadline = Instant::now() + START_TIMEOUT;
            while !Self::is_accepting() {
                assert!(
                    Instant::now() < deadline,
                    "sshd did not accept a connection on port {SSHD_PORT} within \
                     {START_TIMEOUT:?}"
                );
                std::thread::sleep(START_POLL_INTERVAL);
            }
        });

        Self
    }

    /// Whether this daemon knows the `PerSourcePenalties` option at all.
    ///
    /// Asked of the daemon rather than derived from a version number or a
    /// family, because the option arrived in OpenSSH 9.8 and the two polygon
    /// families straddle it — 9.6 on the Debian family, 9.9 on the RHEL one —
    /// and passing an unknown option to the older daemon would stop it starting
    /// rather than degrade.
    ///
    /// Why it is turned off where it exists: the option makes the daemon refuse
    /// an ADDRESS for a while after a failed authentication from it. These
    /// suites assert refusals — a wrong password must not work, a replaced
    /// password must stop working — so every one of those assertions loads a
    /// penalty onto 127.0.0.1, and a later legitimate login from the same test
    /// is then reset at the transport layer before it can even offer a
    /// credential. That failure looks exactly like a broken credential and is
    /// not one; it took a real RHEL polygon run to show it. Turning the penalty
    /// off removes a defence against a brute-force attacker from a daemon that
    /// lives for the length of one test run on loopback, and changes nothing
    /// about whether the credential itself is checked.
    fn supports_per_source_penalties() -> bool {
        Command::new(SSHD_BINARY)
            .args(["-t", "-o", "PerSourcePenalties=no"])
            .output()
            .is_ok_and(|probe| probe.status.success())
    }

    /// Runs `script` as `user` in a real sftp session and returns the outcome.
    ///
    /// `script` is a batch of sftp commands, one per line, fed on standard
    /// input. `BatchMode` is turned back OFF explicitly because `sftp -b`
    /// switches it on, and with it on the client never asks for a password and
    /// every login in this suite would fail as "Permission denied" — a failure
    /// that looks exactly like a broken credential and is not one.
    ///
    /// # Panics
    ///
    /// Panics when the sftp client cannot be run at all.
    pub fn sftp(&self, user: &str, password: &str, script: &str) -> Output {
        let askpass = Self::askpass(password);

        let mut child = Command::new("sftp")
            .args([
                "-P",
                SSHD_PORT,
                "-o",
                "BatchMode=no",
                "-o",
                "StrictHostKeyChecking=no",
                "-o",
                "UserKnownHostsFile=/dev/null",
                "-o",
                "NumberOfPasswordPrompts=1",
                "-b",
                "-",
                &format!("{user}@{SSHD_ADDRESS}"),
            ])
            .env("SSH_ASKPASS", &askpass)
            .env("SSH_ASKPASS_REQUIRE", "force")
            .env("DISPLAY", ":0")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .unwrap_or_else(|error| panic!("the polygon image installs an sftp client: {error}"));

        if let Some(mut pipe) = child.stdin.take() {
            let _ = pipe.write_all(script.as_bytes());
        }

        child
            .wait_with_output()
            .unwrap_or_else(|error| panic!("the sftp client could not be waited for: {error}"))
    }

    /// Asks the daemon to run `command` as `user` over ssh, and returns what
    /// happened.
    ///
    /// This is the "no shell" probe. A login that is only meant to move files
    /// must not be able to execute anything, and the assertion belongs on the
    /// OUTCOME of asking rather than on the presence of a directive in a config
    /// file: a `ForceCommand` line that is in the file but in the wrong block
    /// reads the same and does nothing.
    ///
    /// # Panics
    ///
    /// Panics when the ssh client cannot be run at all.
    pub fn exec(&self, user: &str, password: &str, command: &str) -> Output {
        let askpass = Self::askpass(password);

        Command::new("ssh")
            .args([
                "-p",
                SSHD_PORT,
                "-o",
                "BatchMode=no",
                "-o",
                "StrictHostKeyChecking=no",
                "-o",
                "UserKnownHostsFile=/dev/null",
                "-o",
                "NumberOfPasswordPrompts=1",
                &format!("{user}@{SSHD_ADDRESS}"),
                command,
            ])
            .env("SSH_ASKPASS", &askpass)
            .env("SSH_ASKPASS_REQUIRE", "force")
            .env("DISPLAY", ":0")
            .stdin(Stdio::null())
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs an ssh client: {error}"))
    }

    /// Writes the helper the ssh client asks for the password with.
    ///
    /// The client will not read a password from standard input when standard
    /// input is the batch script, so it is handed one through `SSH_ASKPASS` —
    /// which is how a password reaches a non-interactive ssh without a third
    /// party tool. Mode `0700` and inside a directory only root can read: the
    /// polygon has real unprivileged logins in it, and one of them is the
    /// account this suite is about.
    ///
    /// # Panics
    ///
    /// Panics when the helper cannot be written.
    fn askpass(password: &str) -> PathBuf {
        let directory = std::env::temp_dir().join("maran-polygon-askpass");
        std::fs::create_dir_all(&directory).expect("a temporary directory");
        std::fs::set_permissions(&directory, std::fs::Permissions::from_mode(0o700))
            .expect("the askpass directory must not be readable by the accounts under test");

        let path = directory.join("askpass");
        std::fs::write(&path, format!("#!/bin/sh\nprintf '%s\\n' '{password}'\n"))
            .expect("the askpass helper must be writable");
        std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o700))
            .expect("the askpass helper must be executable");

        path
    }

    /// Whether anything is listening on the suite's port yet.
    fn is_accepting() -> bool {
        std::net::TcpStream::connect(format!("{SSHD_ADDRESS}:{SSHD_PORT}")).is_ok()
    }
}
