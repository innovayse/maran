//! The one sequence a system configuration file's removal may take.

use std::path::Path;

use crate::safe_write::config_tree_lock::config_tree_lock;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, RollbackGuard, SafeWriteError};

/// Removes `target` through the same protocol
/// [`super::render_validate_swap::write_config`] uses to replace one:
/// capture the previous content, unlink, validate the resulting tree, reload
/// — and put the file back if either step refuses.
///
/// Deleting a site's vhost is a configuration change like any other: it can
/// leave the tree invalid (an `upstream` block another file still references,
/// a `server_name` some other config only resolved because this one existed),
/// and a reload of an invalid tree is what stops nginx from starting again
/// after the next reboot. So removal extends the protocol rather than
/// bypassing it — rules/rust.md "Config writes": *an area that needs a
/// variation on this protocol extends `safe_write`, it does not write its own
/// copy.*
///
/// Runs under `config_tree_lock` for the same reason the write does, and one
/// of the three interleavings that lock closes is this function's: a removal
/// that commits while another operation holds the same bytes as its own
/// captured previous content used to be undone by that operation's rollback,
/// which put the deleted site's vhost back.
///
/// A target that is already absent is a success with nothing run: the caller
/// has already decided whether a missing file is `NotFound` or a converged
/// retry, and this function does not reload a web server to achieve a state
/// it is already in.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] when the existing content could
/// not be read, and [`SafeWriteError::Rename`] when the file could not be
/// unlinked — in both cases the target is untouched. Returns
/// [`SafeWriteError::ValidationFailed`] or [`SafeWriteError::ReloadFailed`]
/// with the file restored, [`SafeWriteError::SpawnFailed`] with the same
/// restoration when one of those programs could not be started at all, and
/// [`SafeWriteError::RollbackFailed`] when that restoration also failed.
pub fn remove_config(
    host: &dyn ConfigHost,
    target: &Path,
    validator: &Validator<'_>,
    reload: &Reload<'_>,
) -> Result<(), SafeWriteError> {
    // Held for the whole sequence below, capture included. See
    // `config_tree_lock` for why the unit of serialisation is the host.
    let _guard = config_tree_lock();

    let previous = match std::fs::read(target) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(_) => return Err(SafeWriteError::TemporaryWrite),
    };

    // Armed before the unlink, so every path out of the function below either
    // commits the removal or puts the bytes back.
    // Nothing is swapped IN by a removal, so the state the guard requires to
    // find before it restores is an absent file.
    let mut guard = RollbackGuard::new(target.to_path_buf(), Some(previous), None);

    if std::fs::remove_file(target).is_err() {
        // Nothing was removed, so there is nothing to restore.
        guard.commit();
        return Err(SafeWriteError::Rename);
    }

    let validation = match host.run(validator.program, validator.arguments) {
        Ok(outcome) => outcome,
        Err(error) => return finish_with_rollback(&mut guard, error),
    };
    if validation.status != 0 {
        return finish_with_rollback(
            &mut guard,
            SafeWriteError::ValidationFailed {
                stderr: validation.stderr,
            },
        );
    }

    match host.run(reload.program, reload.arguments) {
        Ok(outcome) if outcome.status == 0 => {
            guard.commit();
            Ok(())
        }
        Ok(outcome) => finish_with_rollback(
            &mut guard,
            SafeWriteError::ReloadFailed {
                stderr: outcome.stderr,
            },
        ),
        Err(error) => finish_with_rollback(&mut guard, error),
    }
}

/// Restores the removed configuration after `original` made it necessary, and
/// turns a failure to restore into [`SafeWriteError::RollbackFailed`].
fn finish_with_rollback(
    guard: &mut RollbackGuard,
    original: SafeWriteError,
) -> Result<(), SafeWriteError> {
    match guard.restore() {
        Ok(()) => Err(original),
        Err(rollback_error) => Err(SafeWriteError::RollbackFailed {
            original_error: Box::new(original),
            rollback_error: rollback_error.to_string(),
        }),
    }
}

#[cfg(test)]
#[path = "../tests/safe_write/remove_config_tests.rs"]
mod tests;
