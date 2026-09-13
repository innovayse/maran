//! Failures of the certificate operations.

use crate::safe_write::SafeWriteError;
use crate::sites::SitesOpError;

/// What can go wrong while installing, removing or generating a certificate.
///
/// One exhaustive list for the whole area, so a caller reads what certificate
/// work can fail on without following a chain of `#[from]`s into other domains
/// (rules/rust.md "Errors").
///
/// **No variant of this enum carries private key material, and none ever may.**
/// A private key is a secret (rules/security.md item 8: *never in logs, error
/// messages, URLs, or git*), and an error is the one value in the agent that is
/// guaranteed to be logged, wrapped and shipped to the panel. That is why
/// [`Self::MalformedPrivateKey`] has no payload at all even though openssl said
/// something useful when it refused.
///
/// Every variant that DOES carry tool output can only be filled from a process
/// that was fed the certificate — `SslHost` splits its two spawning methods
/// exactly along that line, and the key-fed one returns an outcome with no
/// stderr to fill anything with. The rule is therefore enforced by what the
/// types make reachable, not by a filter someone has to remember to apply.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SslOpError {
    /// The private key does not belong to the certificate it was offered with.
    ///
    /// The single most important refusal in this area, and it happens before
    /// anything is written. A mismatched pair passes `nginx -t` — nginx checks
    /// syntax, not that the modulus of one file matches the other — and fails
    /// at the first TLS handshake, so the site goes down at the exact moment it
    /// was supposed to become secure, with the swap already done and the
    /// rollback already disarmed.
    #[error("the private key does not match the certificate")]
    KeyDoesNotMatchCertificate,

    /// The certificate is not a certificate openssl can read.
    #[error("the certificate could not be parsed: {reason}")]
    MalformedCertificate {
        /// What the tool reported, with any echo of the key removed. A
        /// certificate is public, so its own text may appear here.
        reason: String,
    },

    /// The private key is not a private key openssl can read.
    ///
    /// Deliberately empty. Every other failing tool call in the agent carries
    /// its `stderr` for the operator's log, and this one must not: openssl has
    /// been known to echo the input it choked on, and the input here is the
    /// secret. An operator who needs more than this looks at the panel's own
    /// record of what it sent, which never leaves the panel.
    #[error("the private key could not be parsed")]
    MalformedPrivateKey,

    /// The certificate's `notAfter` could not be read or understood.
    ///
    /// The expiry is what the panel schedules renewal from, so a certificate
    /// whose expiry cannot be established is refused rather than installed with
    /// a guessed one — a guess here is a site that silently expires.
    #[error("the certificate's expiry could not be read: {reason}")]
    ExpiryUnreadable {
        /// What the tool printed, or which part of it did not parse.
        reason: String,
    },

    /// openssl could not be run at all.
    #[error("could not run the certificate tool: {reason}")]
    ToolUnavailable {
        /// Why the program could not be started.
        reason: String,
    },

    /// No site with this domain is configured on this host.
    ///
    /// A certificate is installed INTO a site's vhost; there is nothing to
    /// rewire without one, and writing the material anyway would leave key
    /// material on disk that nothing serves and nothing removes.
    #[error("site '{domain}' was not found")]
    SiteNotFound {
        /// The domain that was looked up.
        domain: String,
    },

    /// No certificate is installed for this domain.
    ///
    /// The contract's answer for a removal that has nothing to remove
    /// (`ssl.proto`: *removing when no certificate is installed returns
    /// NotFound*).
    #[error("no certificate is installed for '{domain}'")]
    NotFound {
        /// The domain that was looked up.
        domain: String,
    },

    /// A real certificate is already installed for this domain.
    ///
    /// Only `generate_self_signed` returns this, and it is the whole point of
    /// that operation's idempotency rule: regenerating replaces a previous
    /// self-signed placeholder, and never overwrites the certificate a caller
    /// went to a certificate authority for. Overwriting one would replace a
    /// trusted certificate with one every browser refuses — an outage produced
    /// by a retry.
    #[error("a certificate is already installed for '{domain}'")]
    AlreadyExists {
        /// The domain that already has a certificate.
        domain: String,
    },

    /// The certificate material could not be written to the agent's store.
    #[error("failed to write the certificate material: {reason}")]
    MaterialWrite {
        /// What the write protocol reported. Never the material itself.
        reason: String,
    },

    /// The rewired vhost failed `nginx -t`; the previous vhost was restored.
    #[error("nginx validation failed: {stderr}")]
    NginxValidation {
        /// The validator's standard error, for an operator's log.
        stderr: String,
    },

    /// The vhost was valid but nginx refused to reload it; the previous vhost
    /// was restored.
    #[error("nginx reload failed: {stderr}")]
    ReloadFailed {
        /// The reload command's standard error, for an operator's log.
        stderr: String,
    },

    /// The vhost could not be rendered from its template.
    #[error("failed to render the site configuration: {reason}")]
    Render {
        /// What the renderer reported.
        reason: String,
    },

    /// The configuration file could not be written, swapped in or removed —
    /// every failure of the protocol that is not a validation or reload
    /// refusal.
    #[error("failed to write the site configuration: {reason}")]
    ConfigWrite {
        /// What the write protocol reported, including whether the previous
        /// content was restored.
        reason: String,
    },
}

impl From<SitesOpError> for SslOpError {
    /// Folds the site area's failures into this area's list.
    ///
    /// The three an operator acts on differently keep their meaning — nginx
    /// refusing the content, nginx refusing to reload it, and a template that
    /// did not render — and a site that is not there becomes
    /// [`SslOpError::SiteNotFound`] rather than this area's own
    /// [`SslOpError::NotFound`], which means something else entirely here: no
    /// CERTIFICATE is installed. Everything else collapses into
    /// [`SslOpError::ConfigWrite`], whose `Display` still carries the original
    /// message.
    fn from(error: SitesOpError) -> Self {
        match error {
            SitesOpError::NginxValidation { stderr } => Self::NginxValidation { stderr },
            SitesOpError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            SitesOpError::Render { reason } => Self::Render { reason },
            SitesOpError::NotFound { domain } => Self::SiteNotFound { domain },
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}

impl From<SafeWriteError> for SslOpError {
    /// Folds the config-write protocol's failures into this area's list.
    ///
    /// Reached from the material write, which runs the same protocol as a
    /// vhost write and can fail the same ways.
    fn from(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::NginxValidation { stderr },
            SafeWriteError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            other => Self::MaterialWrite {
                reason: other.to_string(),
            },
        }
    }
}
