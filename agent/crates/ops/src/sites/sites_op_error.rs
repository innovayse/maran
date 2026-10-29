//! Failures of the site operations.

use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::validation::fs::path_error::PathError;

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

    /// Another vhost on this host already serves a host name this site asked
    /// for — its domain or one of its aliases — so the site was not created.
    ///
    /// Separate from [`SitesOpError::AlreadyExists`], which is the idempotent
    /// answer to asking for THIS site again: that one is decided before
    /// anything is written, by finding the site's own vhost. This one is
    /// decided inside the config-write protocol, under its lock, either because
    /// the site's own vhost appeared between that first look and the write (a
    /// concurrent creation of the same domain won), or because `nginx -t`,
    /// reading the whole tree with the new vhost in it, warned that one of its
    /// names is already served elsewhere. nginx exits 0 on that warning and
    /// would otherwise answer the name from whichever file sorts first; the
    /// write treats it as a refusal instead. Either way nothing of the new
    /// vhost is left behind.
    #[error("a host name of this site is already served on this host: {detail}")]
    ServerNameTaken {
        /// What refused: the vhost path that already existed, or nginx's own
        /// warning naming the host.
        detail: String,
        /// The validator's standard error, for an operator's log; empty when
        /// the refusal was an existing file and no validator ran.
        stderr: String,
    },

    /// The site asked for one host name twice — an alias equal to the domain,
    /// or the same alias repeated — so nothing was created.
    ///
    /// Refused before anything is read or written, as the caller's input, and
    /// separate from [`SitesOpError::ServerNameTaken`] on purpose: nginx warns
    /// about a name repeated inside ONE server block in exactly the words it
    /// uses for a name two vhosts share, so without this check the creation
    /// would reach the vhost, be refused there, and be reported as a name
    /// another site already holds — leaving the document root behind and
    /// telling the panel something untrue about the host.
    #[error("the host name '{name}' is asked for more than once")]
    RepeatedHostName {
        /// The name that appeared more than once.
        name: String,
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

    /// The root-owned directory a site's logs live in could not be created.
    ///
    /// Separate from [`SitesOpError::DocumentRoot`] and deliberately so: that
    /// one is a failure to do work AS the account, and this one is a failure of
    /// work root does on its own, in a tree
    /// (`AgentPaths::account_site_log_dir`) no account can reach. An operator
    /// reads them differently — a document-root failure points at the account's
    /// home or its uid, this one points at `/var/log/maran`.
    ///
    /// It is not a cosmetic failure. `nginx -t` OPENS the error-log target, so
    /// a vhost naming a directory that does not exist is refused by the
    /// validator and the whole site operation fails with it.
    #[error("could not create the site log directory: {reason}")]
    LogDirectory {
        /// What the filesystem reported.
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

    /// A program this area ran (`nginx -t`, `systemctl reload`, …) outlived its
    /// deadline or its output ceiling and was killed with its whole process group.
    ///
    /// **Not a verdict**, and so never a [`SitesOpError::NginxValidation`], a
    /// [`SitesOpError::ReloadFailed`] or a [`SitesOpError::ConfigWrite`]: the
    /// program was stopped before it could answer, and a reload that was killed
    /// may still have taken effect. Whatever the operation had written has been
    /// put back by the config-write protocol, as for any other failed step.
    /// Carries the program's path and the bound and never a byte the program
    /// printed.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

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
    /// their own variants and their tool output, and so do a creation's two
    /// refusals, which both become [`SitesOpError::ServerNameTaken`]: in this
    /// area the only creation is a vhost, the only warning it refuses is nginx
    /// reporting a host name served twice, and an existing target is the
    /// site's own domain already served. A bound stays a bound, because it
    /// states no verdict on the content. Everything else collapses into
    /// [`SitesOpError::ConfigWrite`], whose `Display` still carries the
    /// original chain, including a rollback that did not happen.
    fn from(error: SafeWriteError) -> Self {
        match error {
            SafeWriteError::ValidationFailed { stderr } => Self::NginxValidation { stderr },
            SafeWriteError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            SafeWriteError::Bound(bound) => Self::Bound(bound),
            SafeWriteError::TargetExists { target } => Self::ServerNameTaken {
                detail: format!("a vhost already exists at {target}"),
                stderr: String::new(),
            },
            SafeWriteError::RefusedWarning { warning, stderr } => Self::ServerNameTaken {
                detail: warning,
                stderr,
            },
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
    /// refusing to reload a valid pool stays [`SitesOpError::ReloadFailed`], and
    /// a bound stays [`SitesOpError::Bound`] — and everything else collapses into [`SitesOpError::ConfigWrite`], whose
    /// `Display` still carries the original message. The areas do not import
    /// each other's errors any further than this one conversion: a site
    /// operation reports site failures, whatever it had to call to fail.
    fn from(error: crate::php::PhpOpError) -> Self {
        match error {
            crate::php::PhpOpError::PhpVersionNotInstalled { version } => {
                Self::PhpVersionNotInstalled { version }
            }
            crate::php::PhpOpError::ReloadFailed { stderr } => Self::ReloadFailed { stderr },
            crate::php::PhpOpError::Bound(bound) => Self::Bound(bound),
            other => Self::ConfigWrite {
                reason: other.to_string(),
            },
        }
    }
}

#[cfg(test)]
#[path = "../tests/sites/sites_op_error_tests.rs"]
mod tests;
