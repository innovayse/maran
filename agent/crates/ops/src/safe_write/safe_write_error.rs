//! Failures of the config-write protocol.

/// What can go wrong while rendering, validating, swapping in or reloading a
/// system configuration file.
///
/// Every variant from [`Self::ValidationFailed`] onward is a point at which
/// [`super::render_validate_swap::write_config`] has already put the previous
/// content back — the variant tells an operator which step refused, not
/// whether the target file is now safe. [`Self::RollbackFailed`] is the one
/// exception: it means the restoration itself did not happen, which is why it
/// carries both errors rather than collapsing into the first one.
#[derive(Debug, thiserror::Error)]
#[non_exhaustive]
pub enum SafeWriteError {
    /// The configuration text could not be produced.
    ///
    /// Rendering happens in the caller, before [`super::render_validate_swap::write_config`]
    /// is invoked, so this crate never constructs the variant itself; it exists
    /// so a caller that folds a template failure into this same typed surface
    /// has somewhere to put it, rather than inventing a second error type for
    /// the same overall operation.
    #[error("failed to render the configuration: {reason}")]
    Render {
        /// What the renderer reported.
        reason: String,
    },

    /// The temporary file, written beside the target, could not be created or
    /// written to.
    ///
    /// Nothing on disk has changed yet at this point: the target is untouched,
    /// so there is nothing to roll back.
    #[error("could not write the temporary configuration file")]
    TemporaryWrite,

    /// `fsync` of the temporary file or its containing directory failed.
    ///
    /// This is the step that exists purely to survive a crash: without it, a
    /// rename that follows can point at bytes the kernel never flushed to
    /// disk. The target is still untouched here too.
    #[error("could not flush the temporary configuration file to disk")]
    Sync,

    /// The atomic rename of the temporary file onto the target failed.
    ///
    /// A failed rename does not modify the target — either the whole
    /// directory entry moves or it does not — so this is the last variant
    /// that needs no rollback of its own.
    #[error("could not swap the new configuration into place")]
    Rename,

    /// The validating tool (`nginx -t`, `php-fpm -t`, …) rejected the
    /// rendered configuration.
    ///
    /// The previous configuration has been restored, or — when none existed —
    /// the rejected file has been removed, so the next unrelated reload is
    /// unaffected.
    #[error("configuration validation failed: {stderr}")]
    ValidationFailed {
        /// The validator's standard error, for an operator's log.
        stderr: String,
    },

    /// The service reload that follows a successful swap failed.
    ///
    /// This is the harder failure: the new file was syntactically valid and
    /// is already in place when the reload refuses it, so the previous
    /// configuration is restored even though validation passed.
    #[error("configuration reload failed: {stderr}")]
    ReloadFailed {
        /// The reload command's standard error, for an operator's log.
        stderr: String,
    },

    /// The previous configuration could not be restored after a later step
    /// failed.
    ///
    /// Carries both errors because a failure to undo is a different event
    /// from the failure that triggered the undo: an operator paged at 04:00
    /// needs to know the target may now hold neither the old configuration
    /// nor a valid new one.
    #[error("rollback failed after {original_error}: {rollback_error}")]
    RollbackFailed {
        /// The failure that made rollback necessary.
        original_error: Box<SafeWriteError>,
        /// Why restoring the previous content did not succeed.
        rollback_error: String,
    },
}
