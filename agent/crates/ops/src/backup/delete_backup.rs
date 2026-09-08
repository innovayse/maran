//! DeleteBackup: removing one published artifact and its sidecar.

use std::fs::remove_file;
use std::path::Path;

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_lock::take_account_lock;
use crate::backup::backup_root::open_account_directory;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};

/// Removes the backup named `backup_id` for `account` from the local
/// destination under `root`.
///
/// # The three absent-thing cases, decided by the plan and not by convenience
///
/// - **No artifact at all**: [`BackupError::NotFound`], not a failure. The
///   proto's own promise is idempotence, and a caller that deletes the same
///   id twice — because its first response was lost, say — must see the same
///   converged outcome both times, not a failure the second time around.
/// - **An artifact with no sidecar beside it**: the artifact is still
///   removed. The artifact is the thing occupying the disk; its sidecar is
///   only a label describing it, and a label that went missing on its own
///   (an operator's `rm`, a filesystem error on a prior delete that removed
///   the sidecar but not the artifact) is not a reason to leave the disk
///   space behind forever.
/// - **A sidecar with no artifact**: also [`BackupError::NotFound`]. Whether
///   or not a sidecar is lying around, there is no backup here to delete —
///   the artifact's presence is what "this backup exists" means throughout
///   this area (see [`crate::backup::list_backups`]).
///
/// # The lock, and what the caller sees when somebody else holds it
///
/// This takes `account`'s backup lock first, before it resolves the root or
/// stats anything, and answers [`BackupError::AlreadyRunning`] when another
/// operation holds it. That is the agent's half of the plan's R12 — *never
/// delete the backup a restore is reading*. Retention's delete is neither a
/// backup nor a restore, so before this it took nothing and could unlink an
/// artifact between a restore's `--list` pass and its extract pass; the panel
/// deciding the order is a promise no filesystem was keeping.
///
/// **The caller does not wait.** `take_account_lock` is wait-free by design
/// (see its doc), so a delete arriving during a restore returns immediately
/// with a typed refusal the panel retries after its timeout — not an RPC held
/// open for the hours a large restore takes. This is the reason the lock is
/// the honest fix rather than the doc comment being corrected to admit there
/// is none: the cost of taking it is one failed attempt the panel already
/// knows how to repeat, and the cost of not taking it is a restore failing
/// halfway on an artifact the panel believed it was holding.
///
/// The order matters as much as the lock: taken FIRST, so a refusal costs a
/// map lookup rather than a `resolve` and two `stat`s, and so no path here can
/// touch the account's directory while another operation owns it.
///
/// # Errors
///
/// [`BackupError::AlreadyRunning`] as above; [`BackupError::NotFound`] as
/// above, which also covers an account that has no directory under the root at
/// all — nothing is created to find that out, which is
/// `backup_root::open_account_directory`'s whole reason for existing beside
/// the creating one; [`BackupError::ArtifactUndeletable`] when the
/// artifact is present but could not be removed (permissions, a concurrent
/// unmount — something the filesystem itself is refusing, since the lock taken
/// above now really does rule out a concurrent backup or restore of this
/// account).
pub fn delete_backup(
    root: &LocalBackupRoot,
    account: &AccountName,
    backup_id: &BackupId,
) -> Result<(), BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let outcome = match open_account_directory(root, account) {
        Ok(Some(directory)) => delete_in(&directory, backup_id),
        Ok(None) => Err(BackupError::NotFound),
        Err(error) => Err(error),
    };

    drop(guard);
    outcome
}

/// The body of [`delete_backup`], with the account's directory injected —
/// split for the same testability reason [`crate::backup::list_backups`]'s
/// body is.
///
/// # Errors
///
/// As documented on [`delete_backup`].
fn delete_in(directory: &Path, backup_id: &BackupId) -> Result<(), BackupError> {
    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    if artifact.symlink_metadata().is_err() {
        return Err(BackupError::NotFound);
    }

    remove_file(&artifact).map_err(|_| BackupError::ArtifactUndeletable)?;

    // The sidecar is removed on a best-effort basis and its absence is never
    // reported: the artifact — the thing that was actually occupying the
    // disk — is already gone by the line above, and refusing to say so
    // because its label could not also be removed would be exactly the wrong
    // way round.
    let _ = remove_file(&sidecar);

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/delete_backup_tests.rs"]
mod tests;
