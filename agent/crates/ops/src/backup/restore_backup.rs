//! RestoreBackup: putting an archive back over a live account.

use std::fs::remove_dir_all;
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_root::prepare_account_directory;
use crate::backup::finalise_restore::{HOME_MODE, finalise_restore};
use crate::backup::model::restore_outcome::RestoreOutcome;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::prepare_restore::prepare_restore;
use crate::backup::replace_databases::replace_databases;
use crate::backup::restore_marker_file::{
    remove_restore_marker, restore_marker_path, write_restore_marker,
};
use crate::backup::restore_placement::RestorePlacement;
use crate::backup::restore_plan::RestorePlan;
use crate::backup::swap_home::swap_home;

/// The uid the scratch chain belongs to, at every level.
const ROOT_UID: u32 = 0;

/// Restores `account` from the artifact `backup_id` names, replacing its home
/// and the databases the panel still knows about.
///
/// # If this fails halfway, what state is the account in, and can the customer
/// still work?
///
/// That question is what this operation is designed around, and it is answered
/// step by step below rather than left for a reader to reconstruct. It has a
/// single dividing line: **the first `DROP DATABASE` is the point of no
/// return.** Everything before it is undone by doing nothing at all —
/// no rename has happened, no database has been touched, and the only traces
/// are directories under the agent's own scratch and staging roots. Everything
/// after it is undone only by reloading a dump this operation took moments
/// earlier, and reloading can itself fail.
///
/// | # | Step | If it fails HERE, the account is |
/// |---|---|---|
/// | 1 | Verify the artifact's SHA-256 against the digest the panel recorded | Untouched. The customer keeps working; nothing was read into anything. |
/// | 2 | Pre-scan the archive's member list and refuse an archive whose layout is not ours | Untouched. Nothing has been unpacked — see "the hostile archive" below. |
/// | 3 | Extract `manifest.json` root-side; refuse an unknown version, another account's archive, or a manifest that disagrees with the sidecar; refuse a database the panel no longer knows | Untouched. |
/// | 4 | Extract `databases/*` root-side into the root-only scratch and check each dump against the manifest's digest | Untouched. |
/// | 5 | Extract `home/*` AS the account into the staging directory | Untouched. The staging tree is removed on the way out. |
/// | 6 | **For each database in turn: dump it for rollback, then drop it, then load the archive's dump.** The first drop is the point of no return | Every database already replaced is reloaded from its own rollback dump, in reverse order, and the operation reports FAILED naming what it put back and what it could not. **The files are still untouched** — the swap has not happened yet, so there is nothing to reverse there. |
/// | 7 | Rename the home aside, then rename the staging tree into its place | Between the two renames the account has no home, for microseconds. If the second rename fails the first is reversed at once and the account has its home back. If the reversal also fails, the error names the exact path the home is parked at, because a message an operator can act on beats a rollback that lies. |
/// | 8 | Re-apply ownership and mode on the home root; remove the parked tree and the scratch | Restored. A failure here is a disk-space or a permissions problem to report, not a failed restore — except the ownership, which is re-applied before anything is removed for the reason in "finalising" below. |
///
/// # Why the rollback dump is taken per database, immediately before its own
/// drop — and not for all of them up front
///
/// Dumping all five before dropping any looks tidier and is worse. A dump is a
/// point in time, and the value of a rollback dump is that it is the state the
/// database was in **at the moment it was dropped**. Taken up front, the fifth
/// database's rollback dump is as old as the first four replacements took —
/// minutes on a real account — and every write the customer's application made
/// in that window is silently lost by the rollback that was supposed to make
/// them whole. It also fails in the wrong direction: taking five dumps first
/// means a failure on the fifth dump has already done nothing, which is fine,
/// but a failure on the fifth *load* rolls back to five stale points instead of
/// five exact ones. Interleaved, each database's rollback dump is separated
/// from its own drop by one call and by nothing else.
///
/// # What this does NOT restore
///
/// A backup here is files plus databases, and a restore is therefore files plus
/// databases. **It is not an undo.** Specifically, it does not re-create or
/// revert:
///
/// - **nginx vhosts and the site rows behind them** — a site deleted since the
///   backup was taken stays deleted, and a site whose configuration changed
///   keeps the new configuration, serving the restored files.
/// - **TLS certificates** — these live outside the home, in the agent's own
///   certificate directory, and a certificate replaced or purged since the
///   backup stays replaced or purged.
///
/// Nor does it restore the account itself, its SFTP logins, its cron entries'
/// panel rows, or anything else whose truth is a row in the panel's database
/// rather than a file in the home. Those are named here, and not only in the
/// UI, because the failure this warning exists against is a reader of THIS
/// function concluding that "restore" means "put everything back the way it
/// was" and building a feature on that belief.
///
/// # The hostile archive: which mechanism refuses it, and which is behind it
///
/// A member named `../../etc/cron.d/pwn` is refused by the **pre-scan** in step
/// 2, before `tar` is asked to extract anything at all. The pre-scan is the
/// brace: it reads names only, judges the whole archive, and refuses it entire
/// — so not one member of an archive holding that name is unpacked, including
/// the members that looked ordinary.
///
/// `tar`'s own stripping of a leading `/` and its refusal of a `..` component
/// are the **belt behind that brace**: a second, per-member defence, built by
/// somebody else, that this agent guarantees stays armed by never passing
/// `--absolute-names` — `ExtractSpec` has no field that could. The belt is not
/// the reason the brace can be relaxed; it is the reason a defect in the brace
/// is not immediately a root write at a path an attacker named.
///
/// # What this operation deliberately does not do
///
/// It does **not** take the pre-restore backup that Decision 2 puts at step 2
/// of the sequence. That step is a whole `create_backup`, which needs the
/// database catalog seam, and composing two operations is the service layer's
/// job in this crate, not an operation's — `ops::backup` does not call
/// `ops::db` and does not call itself. The caller takes it before calling here.
/// This is written down rather than assumed, because a reader who believed the
/// pre-restore backup happened inside this function would have no reason to
/// check that anyone takes it.
///
/// # Errors
///
/// Every restore-side variant of [`BackupError`]. The ones that matter to a
/// customer are [`BackupError::RolledBack`] (the databases are as they were),
/// [`BackupError::RolledBackPartially`] (some are neither state, and it names
/// them) and [`BackupError::HomeParkedAt`] (the home exists, at the path in the
/// message). [`BackupError::FreeSpace`] is the refusal at the free-space floor
/// (issue #148), decided on the home filesystem before anything is staged.
// Nine parameters, and each one is a different authority. Folding them into a
// request struct was considered and rejected: the struct would be built by the
// service layer, one field at a time, and a field left at its default there is
// exactly the kind of omission this operation must not have — an empty
// `allowed_databases` would silently restore nothing, and a blank
// `expected_sha256` would fail closed only by luck. Named parameters cannot be
// forgotten.
#[allow(clippy::too_many_arguments)]
pub fn restore_backup(
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    root: &LocalBackupRoot,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    home_group: u32,
    floor: &FreeSpaceFloor,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let directory = prepare_account_directory(root, account)?;
    // Asked through the host seam rather than through `AccountIds::resolve`
    // directly, because it is asked a SECOND time inside `perform` and the two
    // answers are compared. A question asked twice has to be askable of a
    // test.
    let ids = host.account_identity(account);
    let outcome = ids.and_then(|ids| {
        let placement = RestorePlacement {
            home_root: PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT),
            scratch_base: PathBuf::from("/"),
            scratch_root: PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
            scratch: AgentPaths::backup_scratch_dir(backup_id),
            scratch_owner: ROOT_UID,
            staging: AgentPaths::restore_staging_dir(account, backup_id),
            previous: AgentPaths::restore_previous_dir(account, backup_id),
            marker: restore_marker_path(account, backup_id),
            owner: ids.uid,
            account_group: ids.gid,
            group: home_group,
            floor: *floor,
        };

        restore_in(
            &placement,
            host,
            account,
            backup_id,
            &directory,
            allowed_databases,
            expected_sha256,
            sink,
        )
    });

    drop(guard);
    outcome
}

/// The body of [`restore_backup`], with every path and id injected.
///
/// Split for the reason the creation's body is split: every directory a test
/// can create sits under `/tmp` or under a home, which are exactly the places
/// the real paths are not, so an operation that read [`AgentPaths`] directly
/// could not be exercised at all.
///
/// # Errors
///
/// As documented on [`restore_backup`].
#[allow(clippy::too_many_arguments)]
fn restore_in(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    directory: &Path,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    // Before anything is staged or extracted: the home filesystem must still
    // have the floor left, or the extraction would fill it part-way and leave
    // the account's home on a disk nothing else can write to either.
    placement
        .floor
        .admit(host.available_bytes(&placement.home_root))?;

    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    let prepared = prepare_restore(
        placement,
        host,
        account,
        &artifact,
        &sidecar,
        allowed_databases,
        expected_sha256,
        sink,
    );

    // The staging tree is removed on every path that did not swap it into
    // place, and the scratch on every path at all — written ONCE, on the
    // outside, rather than at each of the dozen places the body below can
    // return early. A cleanup repeated per error path is a cleanup that is
    // missing from one of them, and the scratch holds a full copy of the
    // customer's databases.
    let planned = match prepared {
        Ok(planned) => planned,
        Err(error) => {
            let _ = remove_dir_all(&placement.staging);
            let _ = remove_dir_all(&placement.scratch);
            return Err(error);
        }
    };

    let outcome = perform(placement, host, account, backup_id, &planned, sink);

    let _ = remove_dir_all(&placement.staging);
    let _ = remove_dir_all(&placement.scratch);

    outcome
}

/// Steps 6 to 8: the irreversible half.
///
/// # Errors
///
/// As documented on [`restore_backup`], for steps 6 to 8.
fn perform(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    planned: &RestorePlan,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let total = u32::try_from(planned.databases.len()).unwrap_or(u32::MAX);
    let restored = replace_databases(placement, host, planned, sink)?;

    // The account's identity, asked again — and this is the last moment at
    // which asking it is worth anything, because the next three statements
    // write a marker naming a uid, rename a home into place, and chown it.
    //
    // The uid in `placement` was read when this operation started, which for a
    // large account was hours ago. It is a fact about that moment: `userdel`
    // frees a uid and `useradd` hands the lowest free one to the next account
    // created on this host, so a home chowned to a remembered number can land
    // under a DIFFERENT customer — and the agent's own ownership check compares
    // uids, so every later file operation would agree that the new tenant owns
    // the old one's files.
    //
    // The account lock this operation holds excludes this agent's own
    // deletion, so what this catches is what the lock cannot see: a `userdel`
    // an operator ran by hand, or a second agent binary. It refuses BEFORE the
    // marker and before the first rename, which is the recoverable side of the
    // line — the staging tree is removed by the cleanup the caller runs
    // unconditionally, and the account's home has not been touched.
    let current = host.account_identity(account)?;
    if current.uid != placement.owner || current.gid != placement.account_group {
        return Err(BackupError::AccountIdentityChanged);
    }

    // Written here and nowhere else: AFTER every database has been replaced and
    // BEFORE the first rename. Its position is what gives it its meaning — a
    // marker on the disk is the fact that step 6 finished, which is why a
    // startup reconciliation finishing the swap FORWARD is completing the
    // operation the customer asked for rather than guessing at one. See
    // `recover_restores` and the threat note it names.
    write_restore_marker(
        &placement.marker,
        account,
        backup_id,
        placement.owner,
        placement.group,
        HOME_MODE,
    )?;

    swap_home(placement, account, sink)?;
    finalise_restore(placement, account, sink)?;

    // Last, and only on the fully successful path. Removing it earlier would
    // hand the window back to the defect this whole marker exists to close; not
    // removing it at all is harmless, because the next start classifies the
    // swap as `Completed` and removes it then.
    remove_restore_marker(&placement.marker);

    Ok(RestoreOutcome {
        files_restored: true,
        databases_restored: restored,
        databases_total: total,
    })
}

#[cfg(test)]
#[path = "../tests/backup/restore_backup_tests.rs"]
mod tests;
