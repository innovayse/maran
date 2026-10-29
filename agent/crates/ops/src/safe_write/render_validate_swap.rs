//! The one sequence a system configuration file's write may take.

use std::fs::File;
use std::io::Write as _;
use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;

use crate::safe_write::config_tree_lock::config_tree_lock;
use crate::safe_write::model::write_intent::WriteIntent;
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
/// # The midpoint that argument does not cover
///
/// That recovery is a property of a FAILURE, not of a KILL. Between the rename
/// and the commit below, the target holds content no validator has accepted,
/// and the only thing that would undo it is the [`RollbackGuard`] — which
/// lives in this process's memory and dies with it. A `SIGKILL` there (the
/// kernel's, after the unit's `TimeoutStopSec`) therefore leaves unvalidated
/// content live at the real path with no writer left to undo it. What looks at
/// the next start is a closed list: `agent::server::serve` reconciles
/// interrupted restores, the web tree (`nginx -t`, then a reload when it
/// passes) and the php-fpm pool trees (`php-fpm<version> -t` per installed
/// version, then that version's reload) — and nothing else. The tree is never
/// torn — the temporary file is written and `fsync`ed whole before the rename —
/// so what is live is the caller's complete render; the exposure is that it may
/// be a render the tree rejects, which wedges every later config write on the
/// host and fails `nginx.service` at the next reboot.
///
/// Three trees remain uncovered, and the reason is a property of their
/// validators rather than an omission: the two systemd jail-mount trees and
/// `vsftpd.conf` pass a MUTATING command as their [`Validator`] — `systemctl
/// daemon-reload` and `systemctl restart` — so a startup pass that re-ran the
/// validator to ask "is what is on disk valid?" would restart FTPS and drop
/// every live session in order to ask a question. Closing that costs a PURE
/// validator per tree and there is none to write: `vsftpd` has no `-t` mode at
/// all, and a systemd unit file has no check-without-load. The php-fpm tree was
/// the fourth and is closed: it needed only a version enumeration
/// (`php::list_php_versions`) in front of a validator that was already pure.
/// [`super::model::Validator`]'s own doc comment carries the count and the
/// argument, `validator_tests.rs` DISCOVERS the four mutating sites from this
/// crate's source so the count cannot go stale, and
/// `agent::server::reconcile_web_tree` carries the same remaining list in its
/// doc comment and in its log line with a test holding the two together. The
/// argued ruling on the three, and the one adapter method that would close two of
/// them, are stated in full on [`Validator`] itself.
///
/// The whole sequence runs under `config_tree_lock`, from the capture of the
/// previous content to the commit or the rollback. It has to: the validator
/// reads the whole host's config tree, so between the rename and the commit
/// this function's content is part of every other writer's answer, and the
/// rollback is an undo of a file another writer may since have changed. See
/// that lock for the three interleavings this closes, for why the unit of
/// serialisation is the host, and for the `spawn_blocking` requirement it
/// places on every caller.
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
/// [`SafeWriteError::Bound`] when the validator or the reload outlived its
/// deadline or its output ceiling and was killed with its whole process group:
/// again the same restoration, and the lock is released by the return like on
/// any other exit. A bound is not a verdict, so it is never reported as a failed
/// validation or a failed reload. After a reload bound the files are restored
/// but the running service may already have loaded the new configuration, and
/// nothing reloads again: disk and service can differ until the next reload
/// (the same state a failed reload leaves, now also reachable by timeout). Returns
/// [`SafeWriteError::RollbackFailed`] when a restoration required by one of
/// the above also failed.
pub fn write_config(
    host: &dyn ConfigHost,
    target: &Path,
    contents: &str,
    validator: &Validator<'_>,
    reload: &Reload<'_>,
) -> Result<(), SafeWriteError> {
    swap_in(
        host,
        target,
        contents,
        validator,
        reload,
        &WriteIntent::Replace,
    )
}

/// The protocol itself, shared by [`write_config`] and
/// [`super::create_config::create_config`] so the two cannot drift: one body,
/// and `intent` decides the only two things that differ between replacing a
/// configuration and introducing a new one.
///
/// For [`WriteIntent::Create`] those two things are, in order:
///
/// - **The target must not exist.** Asked twice, and both answers are needed.
///   The capture below runs under the config-tree lock, so it is the answer for
///   every writer in this process; the rename is then `persist_noclobber`
///   (`renameat2(RENAME_NOREPLACE)`, or a link-then-unlink where the
///   filesystem has no such flag), so a file that appeared since — written by
///   something this process-local lock does not reach — is refused by the
///   kernel rather than overwritten. Either refusal is
///   [`SafeWriteError::TargetExists`] with nothing touched.
/// - **A validator that passed may still refuse.** A validator's exit status is
///   not its whole verdict: `nginx -t` exits 0 on a tree in which two vhosts
///   name the same host and only WARNS that it ignored one of them. After a
///   zero exit, the validator's stderr is searched for each of
///   `refused_warnings`, and the first one present refuses the write with
///   [`SafeWriteError::RefusedWarning`] — after the rollback, exactly as a
///   non-zero exit would. The search runs inside the same critical section as
///   the rename, which is what makes it race-free: two creations that would
///   collide are serialised by the lock, and the second one's validator reads
///   a tree that already holds the first.
///
/// The warning check inherits the midpoint [`write_config`] describes, and is
/// worse off in it than the exit status is: a process killed between the rename
/// and this check leaves the new file live, and the startup pass that
/// re-validates the web tree (`agent::server::reconcile_web_tree`) cannot catch
/// it, because the tree it reads exits 0 — the warning is not that pass's
/// refusal. It reloads the colliding file. Nothing on the host knows which of
/// two files naming one host is the newcomer, so this is stated rather than
/// repaired.
///
/// # Errors
///
/// As [`write_config`], plus the two refusals above for a creation.
pub(super) fn swap_in(
    host: &dyn ConfigHost,
    target: &Path,
    contents: &str,
    validator: &Validator<'_>,
    reload: &Reload<'_>,
    intent: &WriteIntent<'_>,
) -> Result<(), SafeWriteError> {
    // Held for the whole sequence below, and released when this function
    // returns. Taken BEFORE the capture, because a capture taken outside it is
    // a capture of a state another writer can have replaced by the time this
    // one renames over it.
    let _guard = config_tree_lock();

    // Capture what is there now, before anything is touched. Nothing after
    // this line is allowed to lose the ability to answer "what was here
    // before".
    let previous = read_existing(target)?;

    // A creation never replaces. Refused here, under the lock and before a
    // temporary file exists, so the refusal leaves the directory exactly as it
    // was found.
    if matches!(intent, WriteIntent::Create { .. }) && previous.is_some() {
        return Err(SafeWriteError::TargetExists {
            target: target.display().to_string(),
        });
    }

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
    let mut guard = RollbackGuard::new(
        target.to_path_buf(),
        previous,
        Some(contents.as_bytes().to_vec()),
    );

    // Atomically rename the temporary file over the target. This happens
    // BEFORE validation — see the doc comment above for why that is the safe
    // order rather than a shortcut. A creation renames without replacing, so
    // the kernel refuses a target that appeared after the capture above.
    let persisted = match intent {
        WriteIntent::Replace => temp.persist(target).map(drop),
        WriteIntent::Create { .. } => temp.persist_noclobber(target).map(drop),
    };
    if let Err(persist_error) = persisted {
        // `persist` on failure returns the temp file back to us; let it drop
        // and clean itself up. The target was never touched by a failed
        // rename, so there is nothing for the guard to undo — and when the
        // rename failed because the target EXISTS, the file there is somebody
        // else's, which is one more reason the guard must not touch it.
        let already_there = persist_error.error.kind() == std::io::ErrorKind::AlreadyExists;
        drop(persist_error);
        guard.commit();
        if already_there {
            return Err(SafeWriteError::TargetExists {
                target: target.display().to_string(),
            });
        }
        return Err(SafeWriteError::Rename);
    }

    // Validate the configuration now that it is the real file at the real
    // path, so the validator sees exactly what nginx would.
    let validation = match host.run(validator.program, validator.arguments, validator.class) {
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

    // A zero exit is the validator's whole verdict only for a replacement. A
    // creation also refuses the warnings its caller named, because the one
    // that matters here — nginx ignoring a duplicated server name — is printed
    // by a validator that then exits 0.
    if let WriteIntent::Create { refused_warnings } = intent
        && let Some(warning) = refused_warnings
            .iter()
            .find(|warning| validation.stderr.contains(warning.as_str()))
    {
        return finish_with_rollback(
            &mut guard,
            SafeWriteError::RefusedWarning {
                warning: warning.clone(),
                stderr: validation.stderr,
            },
        );
    }

    // Reload the service that reads the target so the new configuration
    // takes effect.
    match host.run(reload.program, reload.arguments, CallClass::Reload) {
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
