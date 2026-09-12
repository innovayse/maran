//! What the daemon does about a restore that was killed instead of returning.

use std::fs::{Permissions, read_dir, remove_dir_all, remove_file, rename, set_permissions};
use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _, chown};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::model::restore_marker::RestoreMarker;
use crate::backup::model::restore_recovery::RestoreRecovery;
use crate::backup::model::swap_state::SwapState;
use crate::backup::reap_backup_scratch::reap_backup_scratch;
use crate::backup::restore_marker_file::{
    MARKER_SUFFIX, PARTIAL_MARKER_SUFFIX, read_restore_marker,
};

/// The uid the restore staging root and the bulk scratch must belong to.
const ROOT_UID: u32 = 0;

/// The permission bits no directory this reconciliation trusts may carry.
///
/// Group-write and other-write. `/home/.maran-restore` is `0711` — traversable
/// by everybody by design, because an account's own `tar` runs inside it — so
/// the check cannot be "root-only mode" the way the bulk scratch's is. What it
/// must be is "nobody but root can PUT anything here", and that is exactly these
/// two bits.
const NO_FOREIGN_WRITE: u32 = 0o022;

/// Finishes or abandons every restore this host was killed in the middle of,
/// and reaps the bulk scratch, keeping the rollback dumps.
///
/// # This MUST run before the socket is bound
///
/// It is the whole concurrency argument. `restore_backup` takes a per-account
/// lock, and this function takes the same lock per account before it touches
/// anything — but a lock table lives in one process, and the process whose
/// restore was killed took its locks to the grave. What actually guarantees that
/// no restore is running while this reconciles is that **no rpc has been
/// accepted yet**: `server::serve` calls this ahead of `UnixListener::bind`.
/// The per-account lock on top of that is the structural half — it makes this
/// function correct if a second caller ever appears, and it means recovery
/// cannot run against a live operation for the same account even then. An
/// account whose lock is held is skipped and counted in
/// [`RestoreRecovery::refused`], never waited for.
///
/// # It answers a tally rather than nothing
///
/// Because the sharpest thing to assert about a reconciler is that it did
/// NOTHING to healthy state, and an absence is not observable. See
/// [`RestoreRecovery`].
///
/// # It never fails
///
/// There is no `Result`. Every refusal is a counted, logged decision to leave
/// something alone, and a daemon that would not start because one directory in
/// its staging root was odd is a daemon that has turned one customer's
/// interrupted restore into every customer's outage.
#[must_use]
pub fn recover_restores() -> RestoreRecovery {
    recover_in(
        Path::new(AgentPaths::RESTORE_STAGING_ROOT),
        Path::new(AgentPaths::ACCOUNT_HOME_ROOT),
        Path::new(AgentPaths::BULK_SCRATCH_ROOT),
        ROOT_UID,
    )
}

/// The body of [`recover_restores`], with every root and the expected owner
/// injected.
///
/// Split for the reason every operation in this area is split: a test does not
/// run as uid 0 and owns nothing above its temporary directory, so a function
/// that read [`AgentPaths`] and compared against zero could only ever be
/// exercised on its refusing path — and a gate that has only been shown input it
/// must reject passes just as well once it has been mutated into rejecting
/// everything (rules/testing.md).
pub(crate) fn recover_in(
    staging_root: &Path,
    home_root: &Path,
    scratch_root: &Path,
    owner: u32,
) -> RestoreRecovery {
    let mut recovery = RestoreRecovery::default();

    if trustworthy_root(staging_root, owner) {
        recover_swaps(staging_root, home_root, &mut recovery);
    } else {
        // Not a start failure and not a silent skip: the staging root being
        // something other than root's own is a host an operator has to look at,
        // and the reconciliation refuses the whole root rather than reading one
        // document out of a directory somebody else can write to.
        tracing::error!(
            root = %staging_root.display(),
            "the restore staging root is not root's alone; interrupted restores were NOT reconciled"
        );
        recovery.refused = recovery.refused.saturating_add(1);
    }

    if trustworthy_root(scratch_root, owner) {
        reap_backup_scratch(scratch_root, &mut recovery);
    } else if scratch_root.symlink_metadata().is_ok() {
        tracing::error!(
            root = %scratch_root.display(),
            "the bulk scratch root is not root's alone; it was NOT reaped"
        );
        recovery.refused = recovery.refused.saturating_add(1);
    }

    recovery
}

/// True when `root` is a directory owned by `owner` that nobody else may write
/// into.
///
/// `symlink_metadata`, so a symlink standing where the root should be is seen as
/// a symlink rather than followed to whatever it points at. A missing root is
/// not trustworthy and not an error either — a host that has never run a restore
/// has no staging root, and there is nothing to reconcile.
fn trustworthy_root(root: &Path, owner: u32) -> bool {
    let Ok(metadata) = root.symlink_metadata() else {
        return false;
    };

    metadata.is_dir() && metadata.uid() == owner && metadata.mode() & NO_FOREIGN_WRITE == 0
}

/// Walks the staging root, acting on every marker and on nothing else.
fn recover_swaps(staging_root: &Path, home_root: &Path, recovery: &mut RestoreRecovery) {
    let Ok(entries) = read_dir(staging_root) else {
        tracing::error!(
            root = %staging_root.display(),
            "the restore staging root could not be listed; interrupted restores were NOT reconciled"
        );
        recovery.refused = recovery.refused.saturating_add(1);
        return;
    };

    let mut unmarked: Vec<PathBuf> = Vec::new();

    for entry in entries.flatten() {
        let path = entry.path();
        let name = entry.file_name();
        let Some(name) = name.to_str() else {
            unmarked.push(path);
            continue;
        };

        // The suffix decides which files are OPENED and nothing else. What is
        // done, and to whom, comes out of the document.
        if name.ends_with(PARTIAL_MARKER_SUFFIX) {
            // A marker whose write was itself interrupted. It was never renamed
            // into place, so no rename of a home can have happened after it, and
            // it describes nothing that needs doing.
            let _ = remove_file(&path);
            continue;
        }
        if !name.ends_with(MARKER_SUFFIX) {
            unmarked.push(path);
            continue;
        }

        recover_one(&path, staging_root, home_root, recovery);
    }

    // Left in place, every one of them. Without a marker the account these
    // belong to is only INFERABLE from the name, and a wrong `rename` into
    // `/home` costs a customer their home while litter costs disk. This is the
    // pre-upgrade case: a host killed mid-restore under a build that wrote no
    // marker.
    for path in &unmarked {
        tracing::warn!(
            path = %path.display(),
            "an entry in the restore staging root has no marker; it was left in place for an \
             operator, because which account it belongs to cannot be established as a fact"
        );
    }
    recovery.unmarked_left = u32::try_from(unmarked.len()).unwrap_or(u32::MAX);
}

/// Reads one marker and carries out the answer its state calls for.
fn recover_one(
    marker_path: &Path,
    staging_root: &Path,
    home_root: &Path,
    recovery: &mut RestoreRecovery,
) {
    let Ok(marker) = read_restore_marker(marker_path) else {
        tracing::error!(
            path = %marker_path.display(),
            "a restore marker could not be read or is of an unknown version; nothing was \
             reconciled from it"
        );
        recovery.refused = recovery.refused.saturating_add(1);
        return;
    };

    // Both tokens through their own validators before a single path is built
    // from them. Everything below is computed by this agent from `AgentPaths`
    // and these two values; no string out of the document reaches a `rename`.
    let (Some(account), Some(backup_id)) = (marker.account(), marker.backup_id()) else {
        tracing::error!(
            path = %marker_path.display(),
            "a restore marker names an account or a backup this agent would never have written; \
             nothing was reconciled from it"
        );
        recovery.refused = recovery.refused.saturating_add(1);
        return;
    };

    let Some(guard) = take_account_lock(&account) else {
        tracing::warn!(
            account = %account.as_str(),
            "an interrupted restore was not reconciled: the account's backup lock is held"
        );
        recovery.refused = recovery.refused.saturating_add(1);
        return;
    };

    let home = home_root.join(account.as_str());
    let previous = under(
        staging_root,
        &AgentPaths::restore_previous_dir(&account, &backup_id),
    );
    let staging = under(
        staging_root,
        &AgentPaths::restore_staging_dir(&account, &backup_id),
    );

    apply(
        &SwapState::classify(&home, &previous, &staging),
        &Swap {
            account: &account,
            marker: &marker,
            marker_path,
            home: &home,
            previous: &previous,
            staging: &staging,
        },
        recovery,
    );

    drop(guard);
}

/// Re-roots a path `AgentPaths` composed onto the staging root in use.
///
/// The NAME of each half of a swap is composed by `AgentPaths` and by nothing
/// else — this does not spell `<account>.previous.<id>` a second time, which is
/// how the two spellings would drift. Only the ROOT is injected, so the unit is
/// exercisable outside `/home`; on a real host `staging_root` IS
/// `AgentPaths::RESTORE_STAGING_ROOT` and the join reproduces the path
/// `AgentPaths` already returned.
///
/// A composed path always has a final component, so the fallback is unreachable;
/// it answers the composed path itself rather than panicking, because a root
/// daemon does not panic (rules/rust.md).
fn under(staging_root: &Path, composed: &Path) -> PathBuf {
    composed
        .file_name()
        .map_or_else(|| composed.to_path_buf(), |name| staging_root.join(name))
}

/// One interrupted restore's three directories, its document and its marker.
///
/// A struct because [`apply`] would otherwise take seven parameters of which
/// three are `&Path` and could be passed in any order — and the order is which
/// directory becomes the customer's home.
struct Swap<'a> {
    /// The account, validated, as the marker named it.
    account: &'a AccountName,
    /// The document, for the ownership step 8 has to be able to finish.
    marker: &'a RestoreMarker,
    /// The marker's own path, removed last on every path that acts.
    marker_path: &'a Path,
    /// `/home/<account>`.
    home: &'a Path,
    /// Where the live home was parked.
    previous: &'a Path,
    /// Where the replacement was built.
    staging: &'a Path,
}

/// Carries out the decided answer for one observed state.
///
/// The `match` is exhaustive over [`SwapState`] on purpose: a state added to
/// that enum without an answer here is a compile error, which is the only way a
/// reconciliation stays complete as the operation changes.
fn apply(state: &SwapState, swap: &Swap<'_>, recovery: &mut RestoreRecovery) {
    match state {
        // Nothing moved. The live home is not touched — including in the case
        // this shares with "the in-process reversal already put it back", which
        // wants exactly this answer too.
        SwapState::NotStarted => {
            let _ = remove_dir_all(swap.staging);
            let _ = remove_file(swap.marker_path);
            recovery.swaps_abandoned = recovery.swaps_abandoned.saturating_add(1);
            tracing::warn!(
                account = %swap.account.as_str(),
                "an interrupted restore had not moved the home; its staging tree was removed"
            );
        }

        // The window. Forward, because the marker's existence is a fact that
        // every database was already replaced — see the threat note's
        // "finish forward, not roll back".
        SwapState::Interrupted | SwapState::StagingOnly => finish_forward(swap, recovery),

        // Forward is not available: the replacement home is gone and the
        // customer's own is parked. The one case where this reconciliation
        // rolls back as its FIRST choice, and it is a choice of one.
        SwapState::ParkedOnly => {
            if rename(swap.previous, swap.home).is_ok() {
                let owned = reown(swap);
                let _ = remove_file(swap.marker_path);
                recovery.swaps_rolled_back = recovery.swaps_rolled_back.saturating_add(1);
                tracing::error!(
                    account = %swap.account.as_str(),
                    ownership_reapplied = owned,
                    "an interrupted restore left no replacement home; the account's PREVIOUS home \
                     was put back and the restore must be retried"
                );
            } else {
                recovery.refused = recovery.refused.saturating_add(1);
                tracing::error!(
                    account = %swap.account.as_str(),
                    parked = %swap.previous.display(),
                    "an interrupted restore's parked home could not be put back; it is at the \
                     path named here and needs an operator"
                );
            }
        }

        // The home is in place; only the ownership and the parked tree may be
        // outstanding. Re-applying both is idempotent.
        SwapState::Swapped => {
            let owned = reown(swap);
            let _ = remove_dir_all(swap.previous);
            let _ = remove_file(swap.marker_path);
            recovery.swaps_completed = recovery.swaps_completed.saturating_add(1);
            tracing::warn!(
                account = %swap.account.as_str(),
                ownership_reapplied = owned,
                "an interrupted restore was finished: the home was already in place"
            );
        }

        // Healthy. The ONLY thing done is removing the marker that outlived its
        // own restore. A reconciler that fires on healthy state is worse than
        // none, and this arm is what makes that a decision.
        SwapState::Completed => {
            let _ = remove_file(swap.marker_path);
            recovery.swaps_already_done = recovery.swaps_already_done.saturating_add(1);
        }

        // Something outside this operation re-created the home. Its staging tree
        // is litter and goes; the parked tree is a customer's home and stays,
        // because only a human can say which of the two homes is wanted.
        SwapState::HomeAndParkedBoth => {
            let _ = remove_dir_all(swap.staging);
            let _ = remove_file(swap.marker_path);
            recovery.refused = recovery.refused.saturating_add(1);
            tracing::error!(
                account = %swap.account.as_str(),
                parked = %swap.previous.display(),
                "an interrupted restore found a home that this agent did not put back; the live \
                 home was NOT touched and the previous home is still parked at the path named here"
            );
        }

        // Nothing to recover with, and said loudly rather than passed over.
        SwapState::NothingLeft => {
            let _ = remove_file(swap.marker_path);
            recovery.refused = recovery.refused.saturating_add(1);
            tracing::error!(
                account = %swap.account.as_str(),
                "an interrupted restore left neither a home nor a parked copy of one; this \
                 account has no home directory and the agent has nothing to put back"
            );
        }

        // A symlink or a file where a directory must be. A root process
        // following that is an arbitrary write at a path somebody else chose.
        SwapState::NotDirectories => {
            recovery.refused = recovery.refused.saturating_add(1);
            tracing::error!(
                account = %swap.account.as_str(),
                "an interrupted restore names something that is not a directory; NOTHING was \
                 touched, including the marker"
            );
        }
    }
}

/// Completes the swap the killed process started: staging becomes the home.
///
/// If the forward rename cannot be made, the parked home goes back at once —
/// the same preference `swap_home` encodes in-process, and the reason a
/// customer ends this function with a working home under either outcome.
fn finish_forward(swap: &Swap<'_>, recovery: &mut RestoreRecovery) {
    if rename(swap.staging, swap.home).is_ok() {
        let owned = reown(swap);
        let _ = remove_dir_all(swap.previous);
        let _ = remove_file(swap.marker_path);
        recovery.swaps_completed = recovery.swaps_completed.saturating_add(1);
        tracing::warn!(
            account = %swap.account.as_str(),
            ownership_reapplied = owned,
            "a restore interrupted between its two renames was completed at startup; the account \
             has its restored home"
        );
        return;
    }

    if rename(swap.previous, swap.home).is_ok() {
        let owned = reown(swap);
        let _ = remove_dir_all(swap.staging);
        let _ = remove_file(swap.marker_path);
        recovery.swaps_rolled_back = recovery.swaps_rolled_back.saturating_add(1);
        tracing::error!(
            account = %swap.account.as_str(),
            ownership_reapplied = owned,
            "a restore interrupted between its two renames could not be completed; the account's \
             PREVIOUS home was put back and the restore must be retried"
        );
        return;
    }

    // Neither direction worked, so the marker STAYS: it is the only record of
    // where this account's home is, and the next start must try again rather
    // than find an unexplained parked tree it may not touch.
    recovery.refused = recovery.refused.saturating_add(1);
    tracing::error!(
        account = %swap.account.as_str(),
        parked = %swap.previous.display(),
        "a restore interrupted between its two renames could not be completed OR reversed; the \
         account's home is parked at the path named here and needs an operator"
    );
}

/// Re-applies the home root's ownership and mode from the marker.
///
/// The same step 8 `finalise` performs, and for the same measured reason: an
/// account's home is owned by the account but group-owned by the WEB SERVER's
/// group at `0750`, and a home left with the account's own group serves every
/// site a silent 403. The values come from the document because they are the
/// ones the killed operation resolved from the distro adapter — nothing about
/// the parked or staged tree could be asked for them.
///
/// Answers whether both applied, for the log line. It is not an error: at this
/// point the customer has a home again, which is the outcome that mattered, and
/// a mode that needs fixing is something an operator can see in the journal.
fn reown(swap: &Swap<'_>) -> bool {
    let owned = chown(
        swap.home,
        Some(swap.marker.owner_uid),
        Some(swap.marker.home_gid),
    )
    .is_ok();
    let moded = set_permissions(swap.home, Permissions::from_mode(swap.marker.home_mode)).is_ok();

    owned && moded
}

#[cfg(test)]
#[path = "../tests/backup/recover_restores_tests.rs"]
mod tests;
