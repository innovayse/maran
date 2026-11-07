//! The panel's own web-server surface in the polygon: the production panel
//! vhost, loaded by the real nginx, and a real TLS client that asks it which
//! certificate it presents.
//!
//! # What is real here, and what is rendered by the test
//!
//! The polygon image carries the installer's panel vhost only for the length of
//! a build-time assertion, so nothing is serving it when a suite starts. This
//! fixture therefore renders **the production template itself**
//! (`installer/nginx/maran.conf`, embedded at compile time) with the same four
//! substitutions `installer/lib/80-nginx.sh` makes, and puts the result in the
//! agent's own include directory, which the real nginx reads. The TLS server
//! block, the port-80 challenge block, the certificate and key paths and the
//! directives between them are the shipped ones; only the host name, the api
//! socket path and the directory the file sits in are the test's.
//!
//! The pair the vhost names is read from `AgentPaths::PANEL_TLS_DIRECTORY`, the
//! very files `install_panel_certificate` replaces, and the challenge block
//! aliases `AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY`, the very directory
//! `write_panel_challenge` writes.
//!
//! # What it cannot observe, said plainly
//!
//! **UNOBSERVED HERE:** the api behind the vhost (the upstream socket is never
//! connected to, so `/api/` and `/health` are not exercised), the installer's
//! own placement of the vhost under `conf.d` (this file sits in the agent's
//! include directory, which nginx reads the same way), and any public
//! authority: the certificates the tests serve come from a throwaway root.

use std::io::Write as _;
use std::os::unix::fs::PermissionsExt as _;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::DistroAdapter;

/// The production panel vhost, byte for byte as it is shipped.
const PANEL_VHOST_TEMPLATE: &str = include_str!(concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../../installer/nginx/maran.conf"
));

/// The file name the rendered vhost is given inside the agent's include
/// directory. Distinct from the installer's `maran.conf` so a vhost an earlier
/// build-time assertion left behind can never be the one this suite loads.
const VHOST_FILE_NAME: &str = "panel-polygon.conf";

/// The port the panel's TLS block listens on, as the shipped vhost spells it.
const TLS_PORT: u16 = 8443;

/// The api socket path substituted into the vhost. nginx never connects to an
/// upstream while parsing, and no test here asks for `/api/`.
const API_SOCKET: &str = "/run/maran/polygon-api.sock";

/// The directory the panel's nginx logs are written under, which `nginx -t`
/// opens files in but does not create.
const LOG_DIRECTORY: &str = "/var/log/maran";

/// How long one TLS handshake attempt is given.
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(15);

/// How long [`PolygonPanelHost::presented_certificate_until`] keeps asking.
///
/// `nginx -s reload` returns when the signal is sent, not when the new workers
/// accept connections, so a handshake taken the instant a reload returns can
/// still be answered by the old ones.
const SERVE_DEADLINE: Duration = Duration::from_secs(20);

/// How long to wait between two polls.
const POLL_INTERVAL: Duration = Duration::from_millis(100);

/// The panel vhost loaded into the polygon's nginx, and the state it needs.
///
/// Everything it creates or replaces is put back by [`Drop`], a panic included:
/// the previous contents of the panel's certificate pair, the vhost file, and
/// each directory it had to make.
pub struct PolygonPanelHost {
    /// The host name the vhost answers to and a client asks for by SNI.
    name: String,
    /// The distribution adapter, for the binaries.
    distro: &'static dyn DistroAdapter,
    /// Where the rendered vhost sits.
    vhost: PathBuf,
    /// What the certificate file held before this fixture, if anything.
    previous_certificate: Option<Vec<u8>>,
    /// What the key file held before this fixture, if anything.
    previous_key: Option<Vec<u8>>,
    /// Directories this fixture created, in creation order.
    created_directories: Vec<PathBuf>,
}

impl PolygonPanelHost {
    /// Puts `certificate_pem` and `private_key_pem` at the panel's paths,
    /// renders the production vhost for `name`, has the real nginx accept and
    /// load it, and waits until the TLS block answers a handshake.
    ///
    /// # Panics
    ///
    /// Panics when a directory or file cannot be written, when `nginx -t`
    /// refuses the tree, or when the TLS block does not answer in time.
    pub fn bring_up(
        distro: &'static dyn DistroAdapter,
        name: &str,
        certificate_pem: &str,
        private_key_pem: &str,
    ) -> Self {
        let mut host = Self {
            name: name.to_owned(),
            distro,
            vhost: Path::new(AgentPaths::NGINX_INCLUDE_DIRECTORY).join(VHOST_FILE_NAME),
            previous_certificate: std::fs::read(Self::certificate_path()).ok(),
            previous_key: std::fs::read(Self::key_path()).ok(),
            created_directories: Vec::new(),
        };

        host.make_directory(Path::new(LOG_DIRECTORY), 0o750);
        host.make_directory(Path::new(AgentPaths::PANEL_TLS_DIRECTORY), 0o700);
        host.make_directory(Path::new(AgentPaths::PANEL_ACME_CHALLENGE_DIRECTORY), 0o755);

        write_file(&Self::key_path(), private_key_pem, 0o600);
        write_file(&Self::certificate_path(), certificate_pem, 0o644);
        write_file(&host.vhost, &host.rendered_vhost(), 0o644);

        Self::assert_tree_valid(distro, "the panel vhost the fixture just rendered");
        crate::polygon_web::PolygonWeb::ensure_running(distro);
        // Started or not, reloaded: a master that was already running read its
        // tree before this file existed.
        crate::polygon_web::PolygonWeb::reload(distro);

        let served = host.presented_certificate_until(|presented| !presented.is_empty());
        assert!(
            !served.is_empty(),
            "the panel's TLS block must answer a handshake on port {TLS_PORT} in the polygon"
        );

        host
    }

    /// The host name the vhost answers to.
    pub fn name(&self) -> &str {
        &self.name
    }

    /// Where the rendered vhost sits, as `nginx -T` names it.
    pub fn vhost_path(&self) -> &Path {
        &self.vhost
    }

    /// Where the panel's certificate is read from.
    pub fn certificate_path() -> PathBuf {
        Path::new(AgentPaths::PANEL_TLS_DIRECTORY).join(AgentPaths::PANEL_CERTIFICATE_FILE)
    }

    /// Where the panel's private key is read from.
    pub fn key_path() -> PathBuf {
        Path::new(AgentPaths::PANEL_TLS_DIRECTORY).join(AgentPaths::PANEL_KEY_FILE)
    }

    /// The bytes of the pair on disk right now, certificate first.
    ///
    /// # Panics
    ///
    /// Panics when either file is missing, which would itself be the finding.
    pub fn pair_on_disk() -> (Vec<u8>, Vec<u8>) {
        (
            std::fs::read(Self::certificate_path()).expect("the panel certificate must exist"),
            std::fs::read(Self::key_path()).expect("the panel key must exist"),
        )
    }

    /// Fails the test with nginx's own words when `nginx -t` refuses the tree.
    ///
    /// # Panics
    ///
    /// Panics when the validator cannot run or refuses.
    pub fn assert_tree_valid(distro: &dyn DistroAdapter, what: &str) {
        let checked = Command::new(distro.nginx_binary())
            .arg("-t")
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            checked.status.success(),
            "nginx -t must accept the tree ({what}):\n{}",
            String::from_utf8_lossy(&checked.stderr)
        );
    }

    /// The configuration nginx itself reports having loaded, from `nginx -T`.
    ///
    /// The server's own answer to "which files do you read", which is the
    /// artefact the system uses and not the template it was rendered from.
    ///
    /// # Panics
    ///
    /// Panics when `nginx -T` cannot run or refuses.
    pub fn effective_configuration(&self) -> String {
        let dumped = Command::new(self.distro.nginx_binary())
            .arg("-T")
            .output()
            .expect("the polygon image installs nginx");
        assert!(
            dumped.status.success(),
            "nginx -T must succeed:\n{}",
            String::from_utf8_lossy(&dumped.stderr)
        );

        String::from_utf8_lossy(&dumped.stdout).into_owned()
    }

    /// The serial and SHA-256 fingerprint of the certificate the TLS block
    /// presents on a fresh handshake, or an empty string when nothing answered.
    pub fn presented_certificate(&self) -> String {
        self.handshake(None)
            .map(|(identity, _)| identity)
            .unwrap_or_default()
    }

    /// Handshakes until `accepted` is satisfied by what is presented or the
    /// deadline passes, and returns the LAST thing presented either way.
    ///
    /// It returns instead of panicking so a certificate that never arrives
    /// fails at the call site with the wrong serial printed.
    pub fn presented_certificate_until(&self, accepted: impl Fn(&str) -> bool) -> String {
        let deadline = Instant::now() + SERVE_DEADLINE;
        let mut last = self.presented_certificate();

        while !accepted(&last) && Instant::now() < deadline {
            std::thread::sleep(POLL_INTERVAL);
            last = self.presented_certificate();
        }

        last
    }

    /// Whether a client trusting only `root` and checking the host name accepts
    /// what the TLS block presents: the chain verifies and covers the name.
    pub fn handshake_verifies_against(&self, root: &Path) -> bool {
        self.handshake(Some(root))
            .is_some_and(|(_, report)| report.contains("Verify return code: 0 (ok)"))
    }

    /// The serial and SHA-256 fingerprint of the first certificate in `pem`,
    /// in the form [`Self::presented_certificate`] reports them, for comparing
    /// what is served with what was installed.
    ///
    /// # Panics
    ///
    /// Panics when openssl cannot read the certificate.
    pub fn identity_of(distro: &dyn DistroAdapter, pem: &str) -> String {
        identity_of_pem(distro, pem).expect("openssl must read the certificate it is handed")
    }

    /// Generates a self-signed pair for `name`, the stand-in for what the
    /// installer leaves before any issuance. Returns the certificate and key.
    ///
    /// # Panics
    ///
    /// Panics when openssl refuses.
    pub fn self_signed_pair(distro: &dyn DistroAdapter, name: &str) -> (String, String) {
        let directory = tempfile::tempdir().expect("a scratch directory can be made");
        let certificate = directory.path().join("self.crt");
        let key = directory.path().join("self.key");
        let made = Command::new(distro.openssl_binary())
            .args([
                "req",
                "-x509",
                "-newkey",
                "rsa:2048",
                "-nodes",
                "-days",
                "2",
                "-subj",
                &format!("/CN={name}"),
                "-addext",
                &format!("subjectAltName=DNS:{name}"),
                "-keyout",
            ])
            .arg(&key)
            .arg("-out")
            .arg(&certificate)
            .output()
            .expect("the polygon image installs openssl");
        assert!(
            made.status.success(),
            "openssl must produce a self-signed pair: {}",
            String::from_utf8_lossy(&made.stderr)
        );

        (
            std::fs::read_to_string(&certificate).expect("the certificate must be readable"),
            std::fs::read_to_string(&key).expect("the key must be readable"),
        )
    }

    /// One TLS handshake against the panel's block. Returns the identity of the
    /// presented certificate and the whole client report, or `None` when the
    /// client could not connect or presented no certificate.
    ///
    /// `trust` adds a root to verify against and the host name to check; the
    /// report then carries openssl's `Verify return code` line.
    fn handshake(&self, trust: Option<&Path>) -> Option<(String, String)> {
        let mut command = Command::new(self.distro.openssl_binary());
        command
            .args(["s_client", "-connect"])
            .arg(format!("127.0.0.1:{TLS_PORT}"))
            .args(["-servername", &self.name]);
        if let Some(root) = trust {
            command
                .arg("-CAfile")
                .arg(root)
                .args(["-verify_hostname", &self.name]);
        }
        // A null stdin ends the session as soon as the handshake is done.
        let mut child = command
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::null())
            .spawn()
            .expect("the polygon image installs openssl");

        let deadline = Instant::now() + HANDSHAKE_TIMEOUT;
        while child.try_wait().ok()?.is_none() {
            if Instant::now() >= deadline {
                let _ = child.kill();
                let _ = child.wait();
                return None;
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        let report = String::from_utf8_lossy(&child.wait_with_output().ok()?.stdout).into_owned();

        let begin = report.find("-----BEGIN CERTIFICATE-----")?;
        let end = report.find("-----END CERTIFICATE-----")? + "-----END CERTIFICATE-----".len();
        let identity = identity_of_pem(self.distro, &report[begin..end])?;

        Some((identity, report))
    }

    /// The production template with the four installer substitutions made.
    fn rendered_vhost(&self) -> String {
        PANEL_VHOST_TEMPLATE
            .replace("__MARAN_SERVER_NAME__", &self.name)
            .replace(
                "__MARAN_CERT_PATH__",
                &Self::certificate_path().display().to_string(),
            )
            .replace(
                "__MARAN_KEY_PATH__",
                &Self::key_path().display().to_string(),
            )
            .replace("__MARAN_API_SOCKET__", API_SOCKET)
    }

    /// Makes `directory` if it is not there, remembering that it did so.
    fn make_directory(&mut self, directory: &Path, mode: u32) {
        if directory.exists() {
            return;
        }
        std::fs::create_dir_all(directory)
            .unwrap_or_else(|error| panic!("{directory:?} must be creatable: {error}"));
        std::fs::set_permissions(directory, std::fs::Permissions::from_mode(mode))
            .unwrap_or_else(|error| panic!("{directory:?} must accept its mode: {error}"));
        self.created_directories.push(directory.to_path_buf());
    }
}

impl Drop for PolygonPanelHost {
    /// Takes the vhost away, puts the pair back as it was, reloads nginx so it
    /// lets go of port 8443, and removes the directories it made.
    ///
    /// Reports instead of panicking: a panic in `drop` while the test is already
    /// panicking aborts the process and hides the failure that started it.
    fn drop(&mut self) {
        if let Err(error) = std::fs::remove_file(&self.vhost) {
            eprintln!(
                "the polygon vhost {:?} could not be removed: {error}",
                self.vhost
            );
        }
        restore(
            &Self::certificate_path(),
            self.previous_certificate.as_deref(),
        );
        restore(&Self::key_path(), self.previous_key.as_deref());

        match Command::new(self.distro.nginx_binary())
            .args(["-s", "reload"])
            .output()
        {
            Ok(reloaded) if reloaded.status.success() => {}
            Ok(reloaded) => eprintln!(
                "nginx refused the cleanup reload:\n{}",
                String::from_utf8_lossy(&reloaded.stderr)
            ),
            Err(error) => eprintln!("nginx could not be asked to reload: {error}"),
        }

        for directory in self.created_directories.iter().rev() {
            if let Err(error) = std::fs::remove_dir_all(directory) {
                eprintln!("the polygon directory {directory:?} could not be removed: {error}");
            }
        }
    }
}

/// Writes `contents` at `path` with `mode`, replacing what is there.
///
/// # Panics
///
/// Panics when the file cannot be written.
fn write_file(path: &Path, contents: &str, mode: u32) {
    std::fs::write(path, contents)
        .unwrap_or_else(|error| panic!("{path:?} must be writable: {error}"));
    std::fs::set_permissions(path, std::fs::Permissions::from_mode(mode))
        .unwrap_or_else(|error| panic!("{path:?} must accept its mode: {error}"));
}

/// Puts `path` back to `previous`, or removes it when there was nothing.
fn restore(path: &Path, previous: Option<&[u8]>) {
    let outcome = match previous {
        Some(bytes) => std::fs::write(path, bytes),
        None => std::fs::remove_file(path),
    };
    if let Err(error) = outcome {
        eprintln!("the polygon file {path:?} could not be restored: {error}");
    }
}

/// The serial and SHA-256 fingerprint of the first certificate in `pem`, as
/// `openssl x509` prints them, or `None` when it cannot read one.
fn identity_of_pem(distro: &dyn DistroAdapter, pem: &str) -> Option<String> {
    let mut child = Command::new(distro.openssl_binary())
        .args(["x509", "-noout", "-serial", "-fingerprint", "-sha256"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .ok()?;
    child.stdin.take()?.write_all(pem.as_bytes()).ok()?;
    let output = child.wait_with_output().ok()?;

    output
        .status
        .success()
        .then(|| String::from_utf8_lossy(&output.stdout).trim().to_owned())
}
