//! Failures of the site operations.

use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::path_error::PathError;

use crate::safe_write::SafeWriteError;

/// What can go wrong while creating, deleting, enabling or disabling a site.
///
/// One exhaustive list for the whole area, so a caller reads what site work
/// can fail on without following a chain of `#[from]`s into other domains
/// (rules/rust.md "Errors"). The `nginx -t` output is carried deliberately and
/// is for an operator's log only — the C# side decides what a hosting customer
/// is told (rules/security.md, role-aware errors).
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SitesOpError {
    /// A site with this domain is already configured on this host.
    ///
    /// Not an accident of the retry contract but the point of it: a second
    /// `create_site` for a live domain must not re-render a vhost somebody may
    /// have a certificate, a document root and traffic on.
    #[error("site '{domain}' already exists")]
    AlreadyExists {
        /// The domain that was asked for.
        domain: String,
    },

    /// No site with this domain is configured on this host.
    #[error("site '{domain}' was not found")]
    NotFound {
        /// The domain that was looked up.
        domain: String,
    },

    /// The rendered config failed `nginx -t`; the previous config was restored.
    #[error("nginx validation failed: {stderr}")]
    NginxValidation {
        /// The validator's standard error, for an operator's log.
        stderr: String,
    },

    /// The configuration was valid but nginx refused to reload it; the
    /// previous config was restored.
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

    /// The document root could not be created as the account.
    ///
    /// Creating it as root is not the fallback: a customer path is touched
    /// under the account's uid or not at all (rules/security.md).
    #[error("could not create the document root as the account: {reason}")]
    DocumentRoot {
        /// What the privilege-dropping helper reported.
        reason: String,
    },

    /// The document root resolved outside the account's home.
    ///
    /// Reached when the path exists but a symlink on the way to it leaves the
    /// home — which is exactly the case a text-level check would pass.
    #[error("the document root is not inside the account's home: {reason}")]
    UnsafeDocumentRoot {
        /// Which containment rule refused.
        reason: String,
    },

    /// The PHP version a site was asked to run is not installed on this host.
    ///
    /// Refused rather than installed on the spot: the contract makes this
    /// `VALIDATION_FAILED`, and an installation takes minutes and streams
    /// progress, so it is its own operation with its own rpc.
    #[error("PHP {version} is not installed on this host")]
    PhpVersionNotInstalled {
        /// The version that was asked for.
        version: String,
    },

    /// The vhost directory could not be read.
    #[error("could not read the site configuration at {path}")]
    ConfigUnreadable {
        /// The path that could not be read.
        path: String,
    },

    /// A site's log could not be read.
    ///
    /// Separate from [`SitesOpError::ConfigUnreadable`] because it is a
    /// read-only operation on a customer's own file and an operator acts on it
    /// differently: nothing was changed, and nothing needs rolling back.
    #[error("could not read the site log at {path}")]
    LogUnreadable {
        /// The path that could not be read.
        path: String,
    },
}

impl From<SafeWriteError> for SitesOpError {
    /// Folds the config-write protocol's failures into this area's list.
    ///
    /// The two failures an operator acts on differently — the validator
    /// refusing the content, and nginx refusing to reload valid content — keep
    /// their own variants and their tool output; everything else collapses
    /// into [`SitesOpError::ConfigWrite`], whose `Display` still carries the
    /// original chain, including a rollback that did not happen.
    fn from(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::NginxValidation { stderr },
            SafeWriteError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}

impl From<PrivError> for SitesOpError {
    /// Reports a failure to do work as the account as a document-root failure,
    /// which is the only thing this area asks of the privilege dropper.
    fn from(error: PrivError) -> Self {
        Self::DocumentRoot {
            reason: error.to_string(),
        }
    }
}

impl From<PathError> for SitesOpError {
    /// Reports a containment refusal separately from every other path problem:
    /// "the path escaped the home" is a security event, not an I/O error.
    fn from(error: PathError) -> Self {
        Self::UnsafeDocumentRoot {
            reason: error.to_string(),
        }
    }
}

impl From<crate::php::PhpOpError> for SitesOpError {
    /// Folds a PHP failure into this area's list.
    ///
    /// The two an operator acts on differently keep their meaning — a missing
    /// version stays [`SitesOpError::PhpVersionNotInstalled`], and php-fpm
    /// refusing to reload a valid pool stays [`SitesOpError::ReloadFailed`] —
    /// and everything else collapses into [`SitesOpError::ConfigWrite`], whose
    /// `Display` still carries the original message. The areas do not import
    /// each other's errors any further than this one conversion: a site
    /// operation reports site failures, whatever it had to call to fail.
    fn from(error: crate::php::PhpOpError) -> Self {
        match error {
            crate::php::PhpOpError::PhpVersionNotInstalled { version } => {
                Self::PhpVersionNotInstalled { version }
            }
            crate::php::PhpOpError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}
