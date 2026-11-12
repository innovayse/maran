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

    /// A Node.js major was asked for on a family that keeps one major at a time (module streams)
    /// while a different major is on the host that a module stream put there (or whose origin could
    /// not be read). Nothing was
    /// installed: switching is a removal and then an install.
    #[error("Node.js {installed} is already on this host, so `{requested}` was not installed")]
    NodeMajorAlreadyInstalled {
        /// The app that was asked for.
        requested: String,
        /// The major that is there, as its digits (`16`), whether or not the catalogue carries it.
        installed: String,
    },

    /// A Node.js is on the host that was not installed through a module stream (the plain
    /// package: no stream of its major is enabled), so the stream commands cannot install over
    /// it or remove it. Nothing was changed. The panel does not remove software it did not
    /// install; the operator does, with the command named.
    #[error(
        "`{requested}` was not changed: Node.js {installed} is on this host and was not installed \
         through a module stream, so the panel does not manage it; remove it yourself with \
         `{command}`, then try again"
    )]
    UnmanagedNodeInstalled {
        /// The app that was asked for.
        requested: String,
        /// The major that is there, as its digits.
        installed: String,
        /// The command an operator runs to remove it, from the family's adapter.
        command: String,
    },

    /// A Node.js module stream is enabled on the host with nothing installed from it (an
    /// operator ran `module enable`), and the install unit resets the module first, which would
    /// drop that choice. Nothing was changed; the panel does not undo a choice it did not make.
    #[error(
        "`{requested}` was not installed: the Node.js module stream {enabled} is enabled on this \
         host and installing would reset that choice; release it yourself with `{command}`, then \
         try again"
    )]
    NodeStreamEnabledByOperator {
        /// The app that was asked for.
        requested: String,
        /// The stream that is enabled, as its digits.
        enabled: String,
        /// The command an operator runs to release it, from the family's adapter.
        command: String,
    },

    /// `/usr/bin/node` is on the host but reports no major, so what is installed cannot be told
    /// and a module install over it is not started (fail closed). Nothing was changed.
    #[error(
        "the Node.js interpreter at {path} does not report its version, so what is installed cannot be told and nothing was changed"
    )]
    NodeInterpreterUnreadable {
        /// The interpreter file.
        path: String,
    },

    /// Another install or removal of a Node.js module stream is running on this host. Nothing
    /// was changed; the same request is safe to repeat once that one ends.
    #[error("another Node.js module install or removal is running on this host")]
    NodejsOperationInProgress,

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
