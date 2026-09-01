//! The in-memory [`SslHost`] the certificate tests decide against, and the
//! material they decide about.
//!
//! Shared by every `*_tests.rs` in this folder through `#[path]`, because the
//! real host writes a private key into `/etc/maran/certificates`, spawns
//! openssl and reloads a live nginx. What a unit test can pin is which files an
//! operation chooses to write, in which order, and when it chooses to write
//! none — not what openssl then says about them.
//!
//! openssl's answers are canned rather than real for a reason worth stating: a
//! test that shelled out to the tool would pass or fail on whether the build
//! container ships it, and would be testing openssl. What is tested here is the
//! agent's decision when openssl says a pair matches, and its decision when
//! openssl says it does not.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::collections::{BTreeMap, BTreeSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use maran_agent_core::command_outcome::CommandOutcome;
use maran_agent_core::validation::name::AccountName;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::fake_site_host::FakeSiteHost;
use crate::sites::{SiteCertificate, SiteHost, SitesOpError};
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::model::key_tool_outcome::KeyToolOutcome;
use crate::ssl::model::self_signed_request::SelfSignedRequest;
use crate::ssl::self_signed_marker::self_signed_marker;
use crate::ssl::ssl_host::SslHost;
use crate::ssl::ssl_op_error::SslOpError;

/// A certificate and key that really do belong together, generated once and
/// committed so the suite never depends on a tool or on today's date. The
/// certificate expires in 2095.
pub(crate) const CERTIFICATE_PEM: &str =
    include_str!("../../../tests/fixtures/example.com.cert.pem");

/// The certificate the fake's `generate_material` hands back — a second, DISTINCT
/// pair, so that installing a real certificate over a generated placeholder is a
/// real replacement in a test rather than a no-op that changes nothing.
pub(crate) const PLACEHOLDER_CERTIFICATE_PEM: &str =
    include_str!("../../../tests/fixtures/placeholder.cert.pem");

/// The private key of [`PLACEHOLDER_CERTIFICATE_PEM`].
pub(crate) const PLACEHOLDER_KEY_PEM: &str =
    include_str!("../../../tests/fixtures/placeholder.key.pem");

/// The private key of [`CERTIFICATE_PEM`].
pub(crate) const KEY_PEM: &str = include_str!("../../../tests/fixtures/example.com.key.pem");

/// A perfectly valid private key that belongs to nothing in this folder — the
/// mismatched half.
pub(crate) const UNRELATED_KEY_PEM: &str =
    include_str!("../../../tests/fixtures/unrelated.key.pem");

/// `notAfter` of [`CERTIFICATE_PEM`], as openssl prints it. The day is padded
/// to two columns, which is the case a split on a single space gets wrong.
pub(crate) const END_DATE_OUTPUT: &str = "notAfter=Feb  9 20:14:26 2095 GMT\n";

/// [`END_DATE_OUTPUT`] as Unix seconds.
pub(crate) const EXPIRY_UNIX: i64 = 3_948_120_866;

/// A public key openssl might print for a certificate.
const CERTIFICATE_PUBLIC_KEY: &str =
    "-----BEGIN PUBLIC KEY-----\nMATCHING\n-----END PUBLIC KEY-----\n";

/// A different public key, for the half that does not belong.
const OTHER_PUBLIC_KEY: &str = "-----BEGIN PUBLIC KEY-----\nDIFFERENT\n-----END PUBLIC KEY-----\n";

/// What the fake did to the machine, in the order it did it.
///
/// The order is a security property here, not a detail: material is written
/// before the vhost points at it, and removed only after the vhost has stopped
/// pointing at it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum Event {
    /// Both files of a certificate were written.
    MaterialWritten,
    /// Both files of a certificate were removed.
    MaterialRemoved,
    /// A vhost was written.
    VhostWritten,
}

/// An [`SslHost`] that keeps the certificate store in memory and answers for
/// openssl.
pub(crate) struct FakeSslHost {
    /// The site half, delegated to rather than re-implemented: what a vhost
    /// write does is already decided, and tested, in the site area.
    sites: FakeSiteHost,
    /// The certificate store: path to content.
    material: Mutex<BTreeMap<PathBuf, String>>,
    /// What the fake did, in order.
    events: Mutex<Vec<Event>>,
    /// Every argv the fake was asked to run, so a test can prove what did NOT
    /// appear in one.
    arguments: Mutex<Vec<Vec<String>>>,
    /// The public key openssl reports for the certificate.
    certificate_public_key: Mutex<String>,
    /// The public key openssl reports for the private key.
    key_public_key: Mutex<String>,
    /// What openssl prints for `-enddate`.
    end_date: Mutex<String>,
    /// What openssl prints for `-subject -issuer`.
    subject_and_issuer: Mutex<String>,
    /// The marker files in the store, by path. Kept apart from the material so
    /// that `stored_count` counts the pair a test cares about.
    markers: Mutex<BTreeSet<PathBuf>>,
    /// A subcommand the fake refuses, with the status and stderr it refuses
    /// with.
    refusal: Mutex<Option<(String, i32, String)>>,
    /// How many marker removals still have to fail before one succeeds — the
    /// transient error a full disk produces, which used to be permanent.
    marker_removals_to_fail: Mutex<usize>,
    /// Whether removing the material fails — the branch a real host reaches
    /// when the store is read-only, or an unlink races another operation.
    removal_fails: Mutex<bool>,
}

impl FakeSslHost {
    /// A host whose openssl agrees the fixture pair matches and whose nginx
    /// accepts everything.
    pub(crate) fn passing() -> Self {
        Self {
            sites: FakeSiteHost::passing(),
            material: Mutex::new(BTreeMap::new()),
            markers: Mutex::new(BTreeSet::new()),
            events: Mutex::new(Vec::new()),
            arguments: Mutex::new(Vec::new()),
            certificate_public_key: Mutex::new(CERTIFICATE_PUBLIC_KEY.to_owned()),
            key_public_key: Mutex::new(CERTIFICATE_PUBLIC_KEY.to_owned()),
            end_date: Mutex::new(END_DATE_OUTPUT.to_owned()),
            subject_and_issuer: Mutex::new(
                "subject=CN = example.com, OU = maran-self-signed\n\
                 issuer=CN = example.com, OU = maran-self-signed\n"
                    .to_owned(),
            ),
            refusal: Mutex::new(None),
            removal_fails: Mutex::new(false),
            marker_removals_to_fail: Mutex::new(0),
        }
    }

    /// Makes openssl report a different public key for the private key, which
    /// is what a mismatched pair looks like.
    pub(crate) fn with_mismatched_key(self) -> Self {
        *self.key_public_key.lock().unwrap() = OTHER_PUBLIC_KEY.to_owned();
        self
    }

    /// Makes openssl report a self-signed certificate whose subject is
    /// `subject` — the customer's own certificate, in whatever shape a test
    /// needs it.
    pub(crate) fn with_self_signed_subject(self, subject: &str) -> Self {
        *self.subject_and_issuer.lock().unwrap() = format!("subject={subject}\nissuer={subject}\n");
        self
    }

    /// Makes openssl report a certificate somebody else signed.
    pub(crate) fn with_authority_signed_certificate(self) -> Self {
        *self.subject_and_issuer.lock().unwrap() =
            "subject=CN = example.com\nissuer=CN = Some Authority R3\n".to_owned();
        self
    }

    /// Makes the next marker removal fail, and only that one.
    pub(crate) fn fail_marker_removal_once(&self) {
        *self.marker_removals_to_fail.lock().unwrap() = 1;
    }

    /// Makes removing the certificate material fail.
    pub(crate) fn fail_material_removal(&self) {
        *self.removal_fails.lock().unwrap() = true;
    }

    /// Makes openssl print `printed` for `-enddate`.
    pub(crate) fn set_end_date(&self, printed: &str) {
        *self.end_date.lock().unwrap() = printed.to_owned();
    }

    /// Makes openssl refuse the subcommand `subcommand` with `stderr`.
    pub(crate) fn refuse(&self, subcommand: &str, stderr: &str) {
        *self.refusal.lock().unwrap() = Some((subcommand.to_owned(), 1, stderr.to_owned()));
    }

    /// Makes `nginx -t` refuse every configuration.
    pub(crate) fn reject_validation(&self, stderr: &str) {
        self.sites.reject_validation(stderr);
    }

    /// The content of a vhost, if the host holds one.
    pub(crate) fn config(&self, path: &Path) -> Option<String> {
        self.sites.config(path)
    }

    /// How many vhost writes or removals reached the protocol.
    pub(crate) fn vhost_writes(&self) -> usize {
        self.sites.writes()
    }

    /// The content of a file in the certificate store.
    pub(crate) fn stored(&self, path: &Path) -> Option<String> {
        self.material.lock().unwrap().get(path).cloned()
    }

    /// How many files the certificate store holds.
    pub(crate) fn stored_count(&self) -> usize {
        self.material.lock().unwrap().len()
    }

    /// What the fake did, in order.
    pub(crate) fn events(&self) -> Vec<Event> {
        self.events.lock().unwrap().clone()
    }

    /// Every argv the fake was asked to run.
    pub(crate) fn arguments(&self) -> Vec<Vec<String>> {
        self.arguments.lock().unwrap().clone()
    }

    /// Whether a marker file sits at `path`.
    pub(crate) fn has_marker(&self, path: &Path) -> bool {
        self.markers.lock().unwrap().contains(path)
    }

    /// Puts a marker at `path`, as an earlier `generate_self_signed` would have.
    pub(crate) fn premark(&self, path: &Path) {
        self.markers.lock().unwrap().insert(path.to_path_buf());
    }

    /// Drops one file from the store, as an interrupted write would leave it.
    pub(crate) fn forget(&self, path: &Path) {
        self.material.lock().unwrap().remove(path);
    }

    /// Records an argv the fake was asked to run.
    fn record(&self, arguments: &[&str]) {
        self.arguments.lock().unwrap().push(
            arguments
                .iter()
                .map(|argument| (*argument).to_owned())
                .collect(),
        );
    }

    /// The canned answer for `arguments`, or the refusal a test installed.
    fn answer(&self, arguments: &[&str]) -> CommandOutcome {
        let subcommand = arguments.first().copied().unwrap_or_default();
        let refusal = self.refusal.lock().unwrap().clone();
        if let Some((refused, status, stderr)) = refusal
            && (refused == arguments.join(" ") || refused == subcommand)
        {
            return CommandOutcome {
                status,
                stdout: String::new(),
                stderr,
            };
        }

        let stdout = match arguments {
            ["x509", "-noout", "-pubkey"] => self.certificate_public_key.lock().unwrap().clone(),
            ["pkey", "-pubout", ..] => self.key_public_key.lock().unwrap().clone(),
            ["x509", "-noout", "-enddate"] => self.end_date.lock().unwrap().clone(),
            ["x509", "-noout", "-subject", "-issuer"] => {
                self.subject_and_issuer.lock().unwrap().clone()
            }
            other => panic!("the fake was asked to run an unexpected argv: {other:?}"),
        };

        CommandOutcome {
            status: 0,
            stdout,
            stderr: String::new(),
        }
    }

    /// Puts `material` in the store as if it had been installed earlier.
    pub(crate) fn preinstall(&self, certificate: &SiteCertificate, material: &CertificateMaterial) {
        let mut store = self.material.lock().unwrap();
        store.insert(
            certificate.certificate_path().to_path_buf(),
            material.certificate_pem().to_owned(),
        );
        store.insert(
            certificate.key_path().to_path_buf(),
            material.private_key_pem().to_owned(),
        );
    }
}

impl SiteHost for FakeSslHost {
    fn read_config(&self, path: &Path) -> Result<Option<String>, SitesOpError> {
        self.sites.read_config(path)
    }

    fn create_directories_as_account(
        &self,
        account: &AccountName,
        directories: &[&Path],
    ) -> Result<(), SitesOpError> {
        self.sites
            .create_directories_as_account(account, directories)
    }

    fn write_config(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        let written = self.sites.write_config(target, contents, validator, reload);
        if written.is_ok() {
            self.events.lock().unwrap().push(Event::VhostWritten);
        }

        written
    }

    fn remove_config(
        &self,
        target: &Path,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError> {
        self.sites.remove_config(target, validator, reload)
    }

    fn resolve_in_account_home(
        &self,
        account: &AccountName,
        relative: &Path,
    ) -> Result<PathBuf, SitesOpError> {
        self.sites.resolve_in_account_home(account, relative)
    }
}

impl SslHost for FakeSslHost {
    /// Answers as openssl would, from the canned outputs above, and records the
    /// argv it was given.
    fn run_with_certificate(
        &self,
        _program: &str,
        arguments: &[&str],
        _certificate_pem: &str,
    ) -> Result<CommandOutcome, SslOpError> {
        self.record(arguments);

        Ok(self.answer(arguments))
    }

    /// The same, through the return type that carries no stderr — which is the
    /// point of the split, and the reason a test cannot even construct a leak
    /// here.
    fn run_with_private_key(
        &self,
        _program: &str,
        arguments: &[&str],
        _private_key_pem: &str,
    ) -> Result<KeyToolOutcome, SslOpError> {
        self.record(arguments);
        let outcome = self.answer(arguments);

        Ok(KeyToolOutcome::new(outcome.status == 0, outcome.stdout))
    }

    fn read_material(&self, path: &Path) -> Result<Option<String>, SslOpError> {
        if self.has_marker(path) {
            return Ok(Some("placeholder\n".to_owned()));
        }

        Ok(self.stored(path))
    }

    fn write_self_signed_marker(&self, path: &Path) -> Result<(), SslOpError> {
        self.premark(path);
        Ok(())
    }

    fn remove_self_signed_marker(&self, path: &Path) -> Result<(), SslOpError> {
        let mut remaining = self.marker_removals_to_fail.lock().unwrap();
        if *remaining > 0 {
            *remaining -= 1;
            drop(remaining);

            return Err(SslOpError::MaterialWrite {
                reason: "no space left on device".to_owned(),
            });
        }
        drop(remaining);

        self.markers.lock().unwrap().remove(path);
        Ok(())
    }

    fn write_material(
        &self,
        certificate: &SiteCertificate,
        material: &CertificateMaterial,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SslOpError> {
        self.preinstall(certificate, material);
        self.events.lock().unwrap().push(Event::MaterialWritten);
        Ok(())
    }

    fn remove_material(
        &self,
        certificate: &SiteCertificate,
        _validator: &Validator<'_>,
        _reload: &Reload<'_>,
    ) -> Result<(), SslOpError> {
        if *self.removal_fails.lock().unwrap() {
            return Err(SslOpError::MaterialWrite {
                reason: "the certificate store is read-only".to_owned(),
            });
        }

        let mut store = self.material.lock().unwrap();
        store.remove(certificate.certificate_path());
        store.remove(certificate.key_path());
        drop(store);
        self.markers
            .lock()
            .unwrap()
            .remove(&self_signed_marker(certificate));
        self.events.lock().unwrap().push(Event::MaterialRemoved);
        Ok(())
    }

    /// Hands back the fixture pair, whatever was asked for.
    ///
    /// What the operation decides — that it asked at all, and what it did with
    /// the answer — is the testable part; generating a real RSA key would make
    /// every run of the suite pay for one.
    fn generate_material(
        &self,
        _program: &str,
        request: &SelfSignedRequest,
    ) -> Result<CertificateMaterial, SslOpError> {
        self.arguments.lock().unwrap().push(vec![
            "req".to_owned(),
            request.subject.clone(),
            request.subject_alternative_name.clone(),
        ]);

        Ok(placeholder_material())
    }
}

/// The fixture pair, as an operation takes it.
pub(crate) fn matching_material() -> CertificateMaterial {
    CertificateMaterial::new(CERTIFICATE_PEM, KEY_PEM)
}

/// The pair the fake's generator produces, distinct from the "real" one above.
pub(crate) fn placeholder_material() -> CertificateMaterial {
    CertificateMaterial::new(PLACEHOLDER_CERTIFICATE_PEM, PLACEHOLDER_KEY_PEM)
}

/// The certificate with a key that is not its own.
pub(crate) fn mismatched_material() -> CertificateMaterial {
    CertificateMaterial::new(CERTIFICATE_PEM, UNRELATED_KEY_PEM)
}
