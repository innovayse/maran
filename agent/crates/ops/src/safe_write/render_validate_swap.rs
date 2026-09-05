//! The one sequence a system configuration file's write may take.

use std::fs::File;
use std::io::Write as _;
use std::path::Path;

use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, RollbackGuard, SafeWriteError};

/// Writes `contents` to `target` through the full config-write protocol:
/// temp file beside the target, `fsync` of the file and its directory,
/// atomic rename, validation, reload — and a rollback to the previous
/// content on a failure at validation or reload.
///
/// `contents` is assumed already rendered: rendering is the caller's job
/// (`maran-templates`), because this crate has no opinion on what a
/// configuration should say, only on how it reaches disk.
///
/// Validation runs AFTER the rename, not before (rules/rust.md "Config
/// writes: render → swap → validate"). The validating tool reads the real
/// config tree by path — `nginx -t` parses `nginx.conf` and everything its
/// includes glob in, and a temporary file named `.tmpXXXXXX` matches no glob
/// and is invisible to it, so validating it in place would parse the OLD
/// tree and prove nothing about the new content. Renaming first is safe
/// because nginx does not read a file until it is asked to: between the
/// rename and the reload the file on disk has changed and the running
/// server has not, so a failed validation is still fully recoverable by
/// restoring the previous content — nothing in the running process needs to
/// be undone.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] or [`SafeWriteError::Sync`] when
/// the temporary file cannot be written or flushed — the target is untouched
/// at this point. Returns [`SafeWriteError::Rename`] when the atomic swap
/// itself fails — the target is untouched, since either the whole directory
/// entry moves or it does not. Returns [`SafeWriteError::ValidationFailed`]
/// when the validator rejects the swapped-in content, with the target
/// restored to its previous state (or removed, if it did not exist before).
/// Returns [`SafeWriteError::ReloadFailed`] when validation passed but the
/// reload command did not, with the previous configuration restored despite
/// the successful swap. Returns [`SafeWriteError::SpawnFailed`] when the
/// validator or the reload could not be STARTED — a missing or unexecutable
/// binary, which is a different job for an operator than either of them
/// refusing — with the same restoration the refusal would have had. Returns
/// [`SafeWriteError::RollbackFailed`] when a restoration required by one of
/// the above also failed.
pub fn write_config(
    host: &dyn ConfigHost,
    target: &Path,
    contents: &str,
    validator: &Validator<'_>,
    reload: &Reload<'_>,
) -> Result<(), SafeWriteError> {
    // Capture what is there now, before anything is touched. Nothing after
    // this line is allowed to lose the ability to answer "what was here
    // before".
    let previous = read_existing(target)?;

    // Write the new content to a temporary file in the SAME DIRECTORY as the
    // target: a rename within one directory is atomic, a rename across
    // filesystems is a copy, and a copy can be read half-written.
    let directory = target.parent().ok_or(SafeWriteError::TemporaryWrite)?;
    let mut temp = tempfile::Builder::new()
        .prefix(".maran-safe-write-")
        .tempfile_in(directory)
        .map_err(|_| SafeWriteError::TemporaryWrite)?;
    temp.write_all(contents.as_bytes())
        .map_err(|_| SafeWriteError::TemporaryWrite)?;

    // `fsync` the temporary file AND its directory. The file's fsync alone is
    // not enough: without the directory's, a crash can leave a directory
    // entry pointing at a rename that landed on disk before the data behind
    // it did.
    temp.as_file()
        .sync_all()
        .map_err(|_| SafeWriteError::Sync)?;
    fsync_directory(directory)?;

    // From here on the target is about to be mutated, so the guard is armed:
    // every exit past this point either commits it (full success) or lets it
    // put the previous content back.
    let mut guard = RollbackGuard::new(target.to_path_buf(), previous);

    // Atomically rename the temporary file over the target. This happens
    // BEFORE validation — see the doc comment above for why that is the safe
    // order rather than a shortcut.
    if let Err(persist_error) = temp.persist(target) {
        // `persist` on failure returns the temp file back to us; let it drop
        // and clean itself up. The target was never touched by a failed
        // rename, so there is nothing for the guard to undo.
        drop(persist_error);
        guard.commit();
        return Err(SafeWriteError::Rename);
    }

    // Validate the configuration now that it is the real file at the real
    // path, so the validator sees exactly what nginx would.
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

    // Reload the service that reads the target so the new configuration
    // takes effect.
    match host.run(reload.program, reload.arguments) {
        Ok(outcome) if outcome.status == 0 => {
            // Full success: the guard's job is done.
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

/// Reads the current bytes of `target`, or `None` when it does not exist.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] when `target` exists but could
/// not be read for a reason other than not existing — the write must not
/// proceed without knowing what it would be overwriting.
fn read_existing(target: &Path) -> Result<Option<Vec<u8>>, SafeWriteError> {
    match std::fs::read(target) {
        Ok(bytes) => Ok(Some(bytes)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(_) => Err(SafeWriteError::TemporaryWrite),
    }
}

/// `fsync`s a directory by opening it and flushing it like any other file
/// descriptor.
///
/// # Errors
///
/// Returns [`SafeWriteError::Sync`] when the directory cannot be opened or
/// flushed.
fn fsync_directory(directory: &Path) -> Result<(), SafeWriteError> {
    File::open(directory)
        .and_then(|handle| handle.sync_all())
        .map_err(|_| SafeWriteError::Sync)
}

/// Restores the previous configuration after `original` made it necessary,
/// and turns a failure to restore into [`SafeWriteError::RollbackFailed`].
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
#[path = "../tests/safe_write/render_validate_swap_tests.rs"]
mod tests;
