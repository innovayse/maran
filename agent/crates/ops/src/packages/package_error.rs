//! Failures of a package unit run or of a read of the host's package sources.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;

/// What can go wrong while installing or removing a package through the root unit, or while
/// asking which packages the host's sources offer.
///
/// The text carried by the variants is for an operator's log only (rules/security.md item 8).
#[derive(Debug, Clone, thiserror::Error)]
#[non_exhaustive]
pub enum PackageError {
    /// A program (`systemctl`, the package query) outlived its deadline and was killed.
    ///
    /// **Not a verdict**: the unit lives in the service manager and may still finish.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

    /// The unit could not be started, or failed for a reason that is this host's own.
    #[error("the package unit failed: {stderr}")]
    PackageManager {
        /// The service manager's standard error, the unit name and a journal hint.
        stderr: String,
    },

    /// A package source answered and refused (an HTTP error status).
    #[error("a package source refused the request: {stderr}")]
    SourceRefused {
        /// As for [`Self::PackageManager`].
        stderr: String,
    },

    /// No package source could be reached.
    #[error("no package source could be reached: {stderr}")]
    SourceUnreachable {
        /// As for [`Self::PackageManager`].
        stderr: String,
    },

    /// None of the host's package sources carries the package.
    #[error("the host's package sources do not carry `{package}`: {stderr}")]
    NotAvailable {
        /// The package the unit was asked for, as the adapter spells it.
        package: String,
        /// As for [`Self::PackageManager`].
        stderr: String,
    },

    /// What the host's package manager offers could not be read.
    #[error("could not read which packages the host offers: {reason}")]
    CatalogueUnavailable {
        /// Why, as text for an operator's log.
        reason: String,
    },
}
