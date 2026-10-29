//! Failures of the config-write protocol.

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;
use maran_distro::AdapterBinary;

/// What can go wrong while rendering, validating, swapping in or reloading a
/// system configuration file.
///
/// Every variant from [`Self::ValidationFailed`] onward is a point at which
/// the shared body of [`super::render_validate_swap::write_config`] and
/// [`super::create_config::create_config`] has already put the previous
/// content back — the variant tells an operator which step refused, not
/// whether the target file is now safe. [`Self::TargetExists`] stands before
/// that line because it is refused before anything is touched. Two of the
/// later ones are exceptions.
/// [`Self::RollbackFailed`] means the restoration itself did not happen, which
/// is why it carries both errors rather than collapsing into the first one.
/// [`Self::SpawnFailed`] says the step did not refuse at all — its program
/// never started — and it stands where the validator or the reload would have
/// stood, so the restoration that follows it is the restoration that step
/// would have had.
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
    /// directory entry moves or it does not — so it needs no rollback of its
    /// own. Neither does [`Self::TargetExists`] below it, the last variant
    /// for which nothing was touched.
    #[error("could not swap the new configuration into place")]
    Rename,

    /// A creation found its target already there, so it changed nothing.
    ///
    /// Only [`super::create_config::create_config`] produces it: a replacement
    /// replaces by definition. Reached either at the capture, under the
    /// config-tree lock, or at the rename, which a creation makes without
    /// replacing so that a file appearing in between is refused by the kernel.
    /// In both cases the file at the target is somebody else's and is left
    /// exactly as it was.
    #[error("a configuration already exists at {target}")]
    TargetExists {
        /// The path that was already taken, for an operator's log.
        target: String,
    },

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

    /// The validator exited 0, but its standard error carried a warning the
    /// caller named as a refusal.
    ///
    /// Only [`super::create_config::create_config`] produces it. It exists
    /// because a zero exit is not every validator's whole verdict: `nginx -t`
    /// accepts a tree in which two vhosts name the same host, warning that it
    /// ignored one of them. The new file has been removed, so the tree is as
    /// it was before the write.
    #[error("configuration validation passed with a refused warning: {warning}")]
    RefusedWarning {
        /// The warning, exactly as the caller named it.
        warning: String,
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

    /// The program could not be started at all — missing binary, not
    /// executable. Distinct from a validator or reload that RAN and refused:
    /// an operator fixing "nginx -t failed" and one installing a missing
    /// package are doing different work, and the error name should say which.
    ///
    /// Nothing on disk has changed when the VALIDATOR could not be started;
    /// when the RELOAD could not be started the new file is already in place
    /// and the protocol restores the previous one, exactly as it does for a
    /// reload that ran and refused.
    #[error("could not run {program}: {reason}")]
    SpawnFailed {
        /// The program that could not be started.
        program: String,
        /// The operating system's reason.
        reason: String,
    },

    /// The validator or the reload outlived its deadline, or wrote more than its
    /// output ceiling, and was killed together with its whole process group.
    ///
    /// **Not a verdict on the configuration**, and so never a
    /// [`Self::ValidationFailed`] or a [`Self::ReloadFailed`]: the program was
    /// stopped before it could answer, and a reload that was killed may still have
    /// taken effect in the service manager. It stands where the validator or the
    /// reload would have stood, so the restoration that follows it is the
    /// restoration that step would have had, and the config-tree lock is released
    /// by the return. Carries the program's path and the bound and never a byte
    /// the program printed.
    #[error(transparent)]
    Bound(#[from] BoundExceeded),

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

impl SafeWriteError {
    /// Folds a failed spawn of `program` into the config-write error surface.
    ///
    /// The one home of a mapping every [`super::ConfigHost::run`] implementation
    /// needs: a bound the core enforced ([`SpawnError::Bound`]) passes through as
    /// [`Self::Bound`], so a deadline stays distinguishable from a refusal, and
    /// every other failure becomes [`Self::SpawnFailed`] naming `program` and the
    /// core's reason. The caller supplies the typed [`AdapterBinary`] so the
    /// `program` field is the binary the host chose, not text parsed back out of
    /// the core's message.
    pub fn from_spawn(program: &AdapterBinary, error: SpawnError) -> Self {
        match error {
            SpawnError::Bound(bound) => Self::Bound(bound),
            other => Self::SpawnFailed {
                program: program.to_string(),
                reason: other.to_string(),
            },
        }
    }
}

#[cfg(test)]
#[path = "../tests/safe_write/safe_write_error_tests.rs"]
mod tests;
