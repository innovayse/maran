//! The one sequence a set of files that must change TOGETHER may take.

use std::fs::{File, Permissions};
use std::io::Write as _;
use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;

use crate::safe_write::model::config_file::ConfigFile;
use crate::safe_write::model::{Reload, Validator};
use crate::safe_write::{ConfigHost, RollbackGuard, SafeWriteError};

/// Writes every file in `files` as ONE change: all of them are renamed into
/// place, then the validator runs once, then the reload runs once — and if
/// either refuses, every one of them is restored.
///
/// The variation on [`super::render_validate_swap::write_config`] that this
/// exists for is not convenience, it is correctness, and the case that forced it
/// is a certificate RENEWAL. A live site's vhost names both
/// `ssl_certificate` and `ssl_certificate_key`. Writing them one at a time
/// through the single-file protocol means that between the two writes the two
/// files are a mismatched pair — and `nginx -t` really does load them:
/// `SSL_CTX_use_PrivateKey_file` compares the key against the certificate and
/// fails with `key values mismatch`. The first write's own validation therefore
/// fails, the guard restores the old key, and a perfectly valid renewal is
/// reported as a validation failure — every sixty to ninety days, for every
/// site, with an nginx error saying exactly what the agent had just proved was
/// false.
///
/// So there is no interim validation and no interim reload: nginx has no opinion
/// on a PEM file's syntax, and asking it for one halfway through an atomic
/// change is the bug. It is asked once, when the set is complete.
///
/// Extends `safe_write` rather than being written inside the area that needed it
/// (rules/rust.md "Config writes": *an area that needs a variation on this
/// protocol extends `safe_write` — it does not write its own copy*).
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] or [`SafeWriteError::Sync`] when a
/// temporary file cannot be written, have its mode set, or be flushed — no
/// target has been touched at that point. Returns [`SafeWriteError::Rename`]
/// when a swap fails, with every target already swapped restored. Returns
/// [`SafeWriteError::ValidationFailed`] or [`SafeWriteError::ReloadFailed`] with
/// ALL targets restored, [`SafeWriteError::SpawnFailed`] with the same
/// restoration when one of those programs could not be started at all, and
/// [`SafeWriteError::RollbackFailed`] when one of those restorations also
/// failed.
pub fn write_config_set(
    host: &dyn ConfigHost,
    files: &[ConfigFile<'_>],
    validator: &Validator<'_>,
    reload: &Reload<'_>,
) -> Result<(), SafeWriteError> {
    if files.is_empty() {
        // Nothing to change, so nothing to validate and nothing to reload: a
        // reload of a live server to achieve a state it is already in is the
        // storm every idempotency rule in this crate exists to prevent.
        return Ok(());
    }

    // Every temporary file is prepared, at its final mode, before ANY target is
    // touched. A failure here leaves the machine exactly as it was found.
    let mut prepared = Vec::with_capacity(files.len());
    for file in files {
        prepared.push(prepare(file)?);
    }

    // From here on the targets are about to be mutated, so the guards are armed
    // together: any exit past this point either commits all of them or restores
    // all of them.
    let mut guards: Vec<RollbackGuard> = prepared
        .iter()
        .map(|(_, file, previous)| RollbackGuard::new(file.target.to_path_buf(), previous.clone()))
        .collect();

    for (temporary, file, _) in prepared {
        if let Err(persist_error) = temporary.persist(file.target) {
            drop(persist_error);
            // The rename that failed did not modify its own target, but earlier
            // ones in this set did — and a half-applied set is the state this
            // whole function exists to make impossible.
            return finish_with_rollback(&mut guards, SafeWriteError::Rename);
        }
    }

    let validation = match host.run(validator.program, validator.arguments) {
        Ok(outcome) => outcome,
        Err(error) => return finish_with_rollback(&mut guards, error),
    };
    if validation.status != 0 {
        return finish_with_rollback(
            &mut guards,
            SafeWriteError::ValidationFailed {
                stderr: validation.stderr,
            },
        );
    }

    match host.run(reload.program, reload.arguments) {
        Ok(outcome) if outcome.status == 0 => {
            for guard in guards {
                guard.commit();
            }
            Ok(())
        }
        Ok(outcome) => finish_with_rollback(
            &mut guards,
            SafeWriteError::ReloadFailed {
                stderr: outcome.stderr,
            },
        ),
        Err(error) => finish_with_rollback(&mut guards, error),
    }
}

/// Writes `file`'s content to a temporary file beside its target, at `file`'s
/// mode, flushed to disk — everything that can be done without touching the
/// target itself.
///
/// The mode is set on the temporary file, before the rename, so the target's
/// name never refers to a file that was briefly readable by anyone else. That
/// ordering is the whole of the private key's protection on disk.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] when the target has no directory,
/// or the temporary file cannot be created, written or chmodded, and
/// [`SafeWriteError::Sync`] when it or its directory cannot be flushed.
fn prepare<'a>(
    file: &'a ConfigFile<'a>,
) -> Result<(tempfile::NamedTempFile, &'a ConfigFile<'a>, Option<Vec<u8>>), SafeWriteError> {
    let previous = read_existing(file.target)?;

    let directory = file.target.parent().ok_or(SafeWriteError::TemporaryWrite)?;
    let mut temporary = tempfile::Builder::new()
        .prefix(".maran-safe-write-")
        .tempfile_in(directory)
        .map_err(|_| SafeWriteError::TemporaryWrite)?;
    temporary
        .write_all(file.contents.as_bytes())
        .map_err(|_| SafeWriteError::TemporaryWrite)?;
    temporary
        .as_file()
        .set_permissions(Permissions::from_mode(file.mode))
        .map_err(|_| SafeWriteError::TemporaryWrite)?;

    temporary
        .as_file()
        .sync_all()
        .map_err(|_| SafeWriteError::Sync)?;
    fsync_directory(directory)?;

    Ok((temporary, file, previous))
}

/// Reads the current bytes of `target`, or `None` when it does not exist.
///
/// # Errors
///
/// Returns [`SafeWriteError::TemporaryWrite`] when `target` exists but could not
/// be read: the write must not proceed without knowing what it would replace.
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

/// Restores every target after `original` made it necessary.
///
/// All of them are attempted even when one fails: leaving the rest swapped in
/// because the first restoration refused would turn one bad file into a set of
/// them.
fn finish_with_rollback(
    guards: &mut [RollbackGuard],
    original: SafeWriteError,
) -> Result<(), SafeWriteError> {
    let mut rollback_failure = None;
    for guard in guards.iter_mut() {
        if let Err(error) = guard.restore() {
            rollback_failure.get_or_insert(error.to_string());
        }
    }

    match rollback_failure {
        None => Err(original),
        Some(rollback_error) => Err(SafeWriteError::RollbackFailed {
            original_error: Box::new(original),
            rollback_error,
        }),
    }
}

#[cfg(test)]
#[path = "../tests/safe_write/write_config_set_tests.rs"]
mod tests;
