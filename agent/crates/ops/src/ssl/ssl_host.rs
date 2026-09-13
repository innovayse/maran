//! The seam between the certificate operations and the machine they run on.

use std::path::Path;

use maran_agent_core::command_outcome::CommandOutcome;

use crate::safe_write::model::{Reload, Validator};
use crate::sites::{SiteCertificate, SiteHost};
use crate::ssl::model::certificate_material::CertificateMaterial;
use crate::ssl::model::key_tool_outcome::KeyToolOutcome;
use crate::ssl::model::self_signed_request::SelfSignedRequest;
use crate::ssl::ssl_op_error::SslOpError;

/// The operating-system operations the certificate module needs on top of the
/// site module's own.
///
/// A supertrait of [`SiteHost`] rather than a second, parallel seam: every
/// certificate operation ends by rewiring a site's vhost, which is a site
/// operation performed with the site area's own renderer and its own write
/// path. One seam therefore covers the whole act, an operation cannot reach the
/// filesystem or a process by another route, and one fake covers all of it in a
/// test.
///
/// The one implementation that touches the machine is
/// [`super::ProcessSslHost`]. It is small on purpose: every decision worth
/// reviewing lives in the operations, where it is tested against a fake, and a
/// test must never really write a key into `/etc/maran/certificates` or reload
/// a live web server.
pub trait SslHost: SiteHost {
    /// Runs `program` with `arguments`, feeding it the CERTIFICATE on standard
    /// input, and returns everything it said.
    ///
    /// The full outcome — stderr included — is safe to expose here and is worth
    /// exposing: a certificate is public, handed to every visitor during the
    /// handshake, so openssl's complaint about one is exactly the evidence an
    /// operator needs. The pairing of this method with
    /// [`Self::run_with_private_key`] is what makes that safety a property of
    /// the type system rather than of anyone's memory.
    ///
    /// Standard input and never an argument or a file, for both methods: an
    /// argv is visible to every user on the host through `/proc/<pid>/cmdline`,
    /// and a temporary file survives a crash and exists for a window at whatever
    /// mode the umask said.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::ToolUnavailable`] when the program cannot be
    /// started or its input cannot be delivered. A non-zero exit is NOT an error
    /// here — it is returned in the outcome, because each caller reads a refusal
    /// differently.
    fn run_with_certificate(
        &self,
        program: &str,
        arguments: &[&str],
        certificate_pem: &str,
    ) -> Result<CommandOutcome, SslOpError>;

    /// Runs `program` with `arguments`, feeding it the PRIVATE KEY on standard
    /// input, and returns the little it is allowed to say.
    ///
    /// A separate method with a crippled return type, because the process on the
    /// other end has been handed the secret: [`KeyToolOutcome`] has no stderr at
    /// all, and its stdout can only be compared, never read. An implementation
    /// MUST discard stderr rather than store it — by the time an operation could
    /// format an error, there must be nothing left to format.
    ///
    /// This replaces a redaction filter that scanned tool output for fragments
    /// of the key. That filter was a blacklist: it could not see the hex primes
    /// `openssl pkey -text` prints, it failed open whenever it guessed the
    /// encoding wrong, and it had to be remembered at each call site. An
    /// invariant a caller has to remember is one a later caller will not.
    ///
    /// # Errors
    ///
    /// As [`Self::run_with_certificate`], and never with anything the tool
    /// printed.
    fn run_with_private_key(
        &self,
        program: &str,
        arguments: &[&str],
        private_key_pem: &str,
    ) -> Result<KeyToolOutcome, SslOpError>;

    /// Reads the PEM at `path`, or reports that there is none.
    ///
    /// How "is a certificate installed for this domain?" is answered, and how
    /// "is this the same material we already hold?" is answered after it: the
    /// contract makes a second installation of identical bytes a no-op success,
    /// and the only way to know they are identical is to look.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::MaterialWrite`] when the file exists but cannot be
    /// read, which must not be mistaken for "nothing installed" — that reading
    /// would have a self-signed placeholder overwrite a real certificate.
    fn read_material(&self, path: &Path) -> Result<Option<String>, SslOpError>;

    /// Writes the key and the certificate into the agent's own store as ONE
    /// change of the config-write protocol.
    ///
    /// One change and not two, and this is the correctness requirement rather
    /// than an optimisation. On a renewal the site's vhost already names both
    /// files; between two separate writes they are a mismatched pair, and
    /// `nginx -t` really does load them — `SSL_CTX_use_PrivateKey_file` compares
    /// the key against the certificate and fails with `key values mismatch`. So
    /// implementations MUST use `safe_write::write_config_set`: both renamed,
    /// then one validation, then one reload, both restored on failure.
    ///
    /// The key lands at mode `0600`, owned by root, with the mode set on the
    /// temporary file BEFORE the rename, so its name never refers to a file that
    /// was briefly wider. It never goes near `fork_as_account` and never inside
    /// a customer's home (`ssl.proto`): it is the agent's file, not the
    /// customer's, and an account that could read it could impersonate the site
    /// to every visitor.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::NginxValidation`] or [`SslOpError::ReloadFailed`]
    /// with the previous material restored, and
    /// [`SslOpError::MaterialWrite`] for every other failure of the protocol —
    /// never carrying the material itself.
    fn write_material(
        &self,
        certificate: &SiteCertificate,
        material: &CertificateMaterial,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SslOpError>;

    /// Records that the material for `path`'s certificate is a placeholder this
    /// agent generated.
    ///
    /// A file in the agent's own store, written after the material it describes.
    /// It is not a configuration — nginx never reads it — so it does not go
    /// through the validate-and-reload protocol; it is a fact the agent keeps
    /// about its own directory, and reloading a web server to record one would
    /// be theatre.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::MaterialWrite`] when the file cannot be written.
    /// The caller must NOT ignore that: a placeholder without its marker is one
    /// this agent will refuse to replace ever again.
    fn write_self_signed_marker(&self, path: &Path) -> Result<(), SslOpError>;

    /// Removes the marker, if it is there.
    ///
    /// Called whenever real material replaces a placeholder. A marker left
    /// behind would hand the customer's new certificate the placeholder's
    /// licence to be destroyed by the next `GenerateSelfSigned` — which is the
    /// one new failure mode a marker file introduces, and the reason this is a
    /// method a caller must handle rather than a best-effort tidy-up.
    ///
    /// Removing a marker that is not there is a success: the state to converge
    /// on is "no marker", and a caller cannot always know whether there was one.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::MaterialWrite`] when the file exists and cannot be
    /// removed.
    fn remove_self_signed_marker(&self, path: &Path) -> Result<(), SslOpError>;

    /// Removes both files, and any marker beside them, through the same
    /// protocol — validating and reloading after each unlink and putting the
    /// file back if either refuses.
    ///
    /// # Errors
    ///
    /// As [`Self::write_material`].
    fn remove_material(
        &self,
        certificate: &SiteCertificate,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SslOpError>;

    /// Generates a self-signed certificate and its key from `request`, and
    /// returns them without either having been part of a site.
    ///
    /// Returned rather than installed, so the generated pair takes exactly the
    /// same path into the store as one the panel obtained from a certificate
    /// authority — one writer of certificate material, one set of modes, one
    /// rollback.
    ///
    /// The two file names openssl writes to are the implementation's business,
    /// because they must be inside a directory only root can traverse and must
    /// be gone before the call returns; everything the certificate SAYS comes
    /// from `request`, which the operation built.
    ///
    /// # Errors
    ///
    /// Returns [`SslOpError::ToolUnavailable`] when openssl cannot be run or
    /// its output cannot be collected, and
    /// [`SslOpError::MalformedCertificate`] when it refuses the request.
    fn generate_material(
        &self,
        program: &str,
        request: &SelfSignedRequest,
    ) -> Result<CertificateMaterial, SslOpError>;
}
