//! A throwaway certificate authority the FTPS suite owns, so a client can be
//! asked to VERIFY the daemon instead of being told to skip verification.
//!
//! # Why a fixture has to own a root
//!
//! Everything else in the suite negotiates TLS with `curl -k`. That proves the
//! channel is encrypted and proves nothing about trust: a client performing
//! ordinary validation has no way to accept a self-signed leaf, so no assertion
//! built on the suite's ordinary material could ever be about a chain. The only
//! way to assert "a client that validates ACCEPTS this daemon" on an isolated
//! host is for the test to control the root the client validates against —
//! which the polygon can, because the material the panel serves is whatever is
//! at the store's paths (`ops::ssl::certificate_state`) and the store is a
//! directory this process is root in.
//!
//! # What it still cannot prove, said plainly
//!
//! **UNOBSERVED HERE: that a PUBLIC client trusts the daemon.** That is a claim
//! about the certificate authorities shipped in other people's trust stores and
//! about a certificate this host cannot obtain — an isolated container reaches
//! no ACME server and owns no public name — so no test here can observe it. What
//! is observable, and is what this authority buys, is the mechanism: a chain
//! rooted in a certificate authority the client knows, covering the name the
//! client asked for, served by the daemon on the panel's own configuration. A
//! publicly-issued certificate differs from this one only in whose key signed
//! it.

use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::validation::web::domain::Domain;
use maran_distro::DistroAdapter;
use maran_ops::sites::SiteCertificate;

/// How long the issued material is valid for.
///
/// Days rather than years, and short on purpose: nothing here outlives the
/// container, and a fixture that mints long-lived material invites someone to
/// reuse it somewhere it would matter.
const VALIDITY_DAYS: &str = "2";

/// The key size every certificate here is issued with.
const KEY_SPEC: &str = "rsa:2048";

/// A certificate authority living in its own directory, able to issue leaf
/// material into the panel's certificate store.
///
/// Dropped with its directory, so one test's root is never what another test is
/// really validating against.
pub struct PolygonCertificateAuthority {
    /// The directory holding the authority's own key and certificate.
    directory: PathBuf,
}

impl PolygonCertificateAuthority {
    /// Creates an authority under `/tmp/<name>`, replacing anything already
    /// there.
    ///
    /// `name` distinguishes two authorities in one run, which is what the
    /// inverse control needs: a client handed the WRONG root must refuse, and
    /// "wrong" is only meaningful when a second, genuinely unrelated authority
    /// exists.
    ///
    /// # Panics
    ///
    /// Panics when the directory cannot be made or `openssl` refuses to produce
    /// the authority's material.
    pub fn create(distro: &dyn DistroAdapter, name: &str) -> Self {
        let directory = Path::new("/tmp").join(name);
        if directory.exists() {
            std::fs::remove_dir_all(&directory)
                .unwrap_or_else(|error| panic!("{directory:?} must be removable: {error}"));
        }
        std::fs::create_dir_all(&directory)
            .unwrap_or_else(|error| panic!("{directory:?} must be creatable: {error}"));

        let authority = Self { directory };

        run_openssl(
            distro,
            &[
                "req".as_ref(),
                "-x509".as_ref(),
                "-newkey".as_ref(),
                KEY_SPEC.as_ref(),
                "-nodes".as_ref(),
                "-days".as_ref(),
                VALIDITY_DAYS.as_ref(),
                "-subj".as_ref(),
                format!("/CN=Maran polygon {name}").as_ref(),
                "-addext".as_ref(),
                "basicConstraints=critical,CA:TRUE".as_ref(),
                "-addext".as_ref(),
                "keyUsage=critical,keyCertSign,cRLSign".as_ref(),
                "-keyout".as_ref(),
                authority.key_path().as_os_str(),
                "-out".as_ref(),
                authority.root_path().as_os_str(),
            ],
            "the authority's own material",
        );

        authority
    }

    /// The root certificate a client is pointed at with `--cacert`.
    pub fn root_path(&self) -> PathBuf {
        self.directory.join("root.pem")
    }

    /// Issues material for `domain` into the panel's own certificate store,
    /// signed by this authority.
    ///
    /// **Written at the store's paths, exactly where `ops::ssl` would put it.**
    /// The point of the tests that use this is that the daemon serves what the
    /// PANEL installed, so the material has to arrive by the panel's own
    /// locations — `SiteCertificate::for_domain`, which has no other constructor
    /// — and not by a path the test invented.
    ///
    /// `fullchain.pem` gets the leaf followed by this authority's certificate,
    /// which is the shape a real issuance has, so the daemon serves a chain
    /// rather than a bare leaf. The name is carried in a `subjectAltName`
    /// extension and not only in the subject, because a client that validates
    /// names has ignored the common name for years and would reject material
    /// that only carried it there.
    ///
    /// # Panics
    ///
    /// Panics when `openssl` refuses, or when the store cannot be written.
    pub fn issue_into_the_panel_store(&self, distro: &dyn DistroAdapter, domain: &Domain) {
        let certificate = SiteCertificate::for_domain(domain);
        let store = certificate
            .certificate_path()
            .parent()
            .expect("the store's paths always have a parent");
        std::fs::create_dir_all(store)
            .unwrap_or_else(|error| panic!("{store:?} must be creatable: {error}"));

        let request = self.directory.join("leaf.csr");
        let leaf = self.directory.join("leaf.pem");
        let extensions = self.directory.join("leaf.ext");
        std::fs::write(
            &extensions,
            format!(
                "subjectAltName=DNS:{name}\nbasicConstraints=critical,CA:FALSE\n\
                 keyUsage=critical,digitalSignature,keyEncipherment\n\
                 extendedKeyUsage=serverAuth\n",
                name = domain.as_str()
            ),
        )
        .unwrap_or_else(|error| panic!("{extensions:?} must be writable: {error}"));

        run_openssl(
            distro,
            &[
                "req".as_ref(),
                "-new".as_ref(),
                "-newkey".as_ref(),
                KEY_SPEC.as_ref(),
                "-nodes".as_ref(),
                "-subj".as_ref(),
                format!("/CN={}", domain.as_str()).as_ref(),
                "-keyout".as_ref(),
                certificate.key_path().as_os_str(),
                "-out".as_ref(),
                request.as_os_str(),
            ],
            "the leaf's key and request",
        );

        run_openssl(
            distro,
            &[
                "x509".as_ref(),
                "-req".as_ref(),
                "-in".as_ref(),
                request.as_os_str(),
                "-CA".as_ref(),
                self.root_path().as_os_str(),
                "-CAkey".as_ref(),
                self.key_path().as_os_str(),
                "-CAcreateserial".as_ref(),
                "-days".as_ref(),
                VALIDITY_DAYS.as_ref(),
                "-extfile".as_ref(),
                extensions.as_os_str(),
                "-out".as_ref(),
                leaf.as_os_str(),
            ],
            "the signed leaf",
        );

        let leaf_pem = std::fs::read_to_string(&leaf)
            .unwrap_or_else(|error| panic!("{leaf:?} must be readable: {error}"));
        let root_pem = std::fs::read_to_string(self.root_path())
            .unwrap_or_else(|error| panic!("the authority's root must be readable: {error}"));
        std::fs::write(
            certificate.certificate_path(),
            format!("{leaf_pem}{root_pem}"),
        )
        .unwrap_or_else(|error| panic!("the store's chain must be writable: {error}"));
    }

    /// The authority's private key.
    fn key_path(&self) -> PathBuf {
        self.directory.join("root.key")
    }
}

impl Drop for PolygonCertificateAuthority {
    /// Takes the authority's directory away, panic or not.
    fn drop(&mut self) {
        if let Err(error) = std::fs::remove_dir_all(&self.directory) {
            eprintln!(
                "the polygon authority {:?} could not be removed: {error}",
                self.directory
            );
        }
    }
}

/// Runs the host's `openssl` with `arguments` and fails the test if it refuses.
///
/// `what` names the material being produced, so a failure says which of the
/// three invocations died rather than only that one did.
///
/// # Panics
///
/// Panics when the binary cannot be run or exits non-zero.
fn run_openssl(distro: &dyn DistroAdapter, arguments: &[&std::ffi::OsStr], what: &str) {
    let outcome = Command::new(distro.openssl_binary())
        .args(arguments)
        .output()
        .unwrap_or_else(|error| panic!("the polygon image installs openssl: {error}"));

    assert!(
        outcome.status.success(),
        "openssl must produce {what}: {}",
        String::from_utf8_lossy(&outcome.stderr)
    );
}
