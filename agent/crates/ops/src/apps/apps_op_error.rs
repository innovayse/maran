//! Failures of the app operations.

use crate::packages::PackageError;
use crate::php::PhpOpError;

/// What can go wrong while listing, installing, removing or controlling an app.
///
/// The text carried by the variants is for an operator's log only (rules/security.md item 8).
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum AppsOpError {
    /// The id is well formed but names an app the panel does not run (a PHP version outside the
    /// supported set). The catalogue is closed: what the agent installs is not the caller's choice.
    #[error("`{app}` is not an app of the catalogue")]
    UnsupportedApp {
        /// The id that was asked for.
        app: String,
    },

    /// The app is not installed, so there is nothing to remove or to control.
    #[error("`{app}` is not installed on this host")]
    NotInstalled {
        /// The id that was asked for.
        app: String,
    },

    /// A site web server was asked for while another one holds the host's sites.
    #[error("`{installed}` already serves this host's sites, so `{requested}` was not installed")]
    WebServerAlreadyInstalled {
        /// The app that was asked for.
        requested: String,
        /// The web server that is already there.
        installed: String,
    },

    /// Something on this host still uses the app.
    #[error("`{app}` is still used: {users}")]
    InUse {
        /// The app that was asked to go.
        app: String,
        /// What uses it, in words an operator reads.
        users: String,
    },

    /// The package manager's dry run of the removal shows it would also remove packages that are
    /// not the app's own, or the panel's front. Nothing was removed.
    #[error("removing `{app}` would also remove {packages}, so it was not started")]
    RemovalWouldRemoveOthers {
        /// The app that was asked to go.
        app: String,
        /// The other packages, in words an operator reads.
        packages: String,
    },

    /// The removal was not started because the package manager's dry run gave no usable answer:
    /// what a removal would take is unknown, and an unknown removal is not run.
    #[error("the removal of `{app}` could not be checked and was not started: {reason}")]
    RemovalNotSimulated {
        /// The app that was asked to go.
        app: String,
        /// Why the dry run gave no answer, for an operator's log.
        reason: String,
    },

    /// A directory whose content decides a destructive step could not be read, so the step was
    /// not taken: "cannot look" is never "nothing there".
    #[error("could not read {path}: {reason}")]
    HostUnreadable {
        /// The directory.
        path: String,
        /// Why, for an operator's log.
        reason: String,
    },

    /// The action cannot be done to this app: it has no service, or the panel's own front would
    /// be taken off the air.
    #[error("`{app}` cannot be {action}: {reason}")]
    ActionRefused {
        /// The app.
        app: String,
        /// The action, as a past participle.
        action: &'static str,
        /// Why not.
        reason: &'static str,
    },

    /// The service manager refused an action or could not be asked.
    #[error("the service manager failed: {stderr}")]
    ServiceManager {
        /// Its standard error, for an operator's log.
        stderr: String,
    },

    /// The package succeeded but the app is still not there.
    #[error("the install of `{app}` succeeded but it is still not installed; {detail}")]
    StillAbsent {
        /// The app.
        app: String,
        /// Where to look.
        detail: String,
    },

    /// The package unit or the package source reading failed.
    #[error(transparent)]
    Package(#[from] PackageError),

    /// A PHP-area operation the app store delegates to failed (installing a PHP version, writing
    /// the Apache include).
    #[error(transparent)]
    Php(#[from] PhpOpError),
}
