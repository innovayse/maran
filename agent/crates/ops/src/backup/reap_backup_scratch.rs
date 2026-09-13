//! Emptying the bulk scratch at startup without destroying the rollback dumps.

use std::fs::{read_dir, remove_dir_all, remove_file};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

use crate::backup::model::restore_recovery::RestoreRecovery;

/// The directory every operation's scratch lives under, inside the bulk root.
///
/// `AgentPaths::backup_scratch_dir` composes `<root>/backup/<id>`; this is that
/// middle component, and it is the only entry directly under the root that this
/// reaper keeps.
const SCRATCH_AREA: &str = "backup";

/// The subdirectory of one operation's scratch holding its pre-drop rollback
/// dumps.
///
/// Must agree with `restore_backup::ROLLBACK_DIRECTORY`. The two are separate
/// constants in separate units by the same argument the manifest's version
/// number gets: this reaper is the one thing in the system whose job is to NOT
/// delete that directory, and importing the name from the module that creates it
/// would make a rename of it silently turn this reaper back into `rm -rf`. A
/// test asserts they agree.
const ROLLBACK_DIRECTORY: &str = "rollback";

/// How many killed restores' rollback dumps survive a start.
///
/// This is the number that answers "what bounds the growth", which is the
/// question `ExecStartPre=-/bin/rm -rf /var/lib/maran-scratch` existed to answer
/// and which removing that line has to answer instead.
///
/// The large half of a scratch — the archive's extracted dumps — is removed
/// unconditionally on every start, and it is reproducible: the artifact is still
/// on the disk with its recorded SHA-256, and a retried restore re-extracts it.
/// What survives is only `rollback/`, and only from a run that was KILLED: every
/// run that returns, successfully or not, removes its own scratch entire.
///
/// Four, and the number is a judgement rather than a measurement. A kill takes
/// down every in-flight restore at once, and more than a handful of concurrent
/// restores on one host is not a thing this product does; while "keep exactly
/// one start's worth" would delete the dumps under an operator who restarted
/// twice while working out what happened. The peak this admits is four accounts'
/// pre-restore database estates, in a root-only `0700` tree, and it takes four
/// separate killed restores to reach it.
///
/// An age bound was the alternative and was not taken: it needs the ambient
/// clock, which rules/testing.md keeps out of logic. Ordering by modification
/// time needs no reading of "now" — only a comparison between two files that
/// both already exist.
const RETAINED_ROLLBACK_SETS: usize = 4;

/// Removes everything under the bulk scratch that a retry could reproduce, and
/// keeps what is the only copy of anything.
///
/// # What the unit file used to do, and why it stopped
///
/// `installer/systemd/maran-agent.service` carried
/// `ExecStartPre=-/bin/rm -rf /var/lib/maran-scratch`. Its argument — a killed
/// operation's leftovers are a copy of a customer's databases on a disk nobody
/// watches — was correct, and this function keeps it. What it did not consider
/// is that `<id>/rollback/` holds the pre-restore state of every database a
/// killed restore had already dropped, and that it is the ONLY copy of it. So
/// the restart an operator performs in order to recover destroyed the material
/// the recovery needs.
///
/// The narrowing could not be done in the unit: the paths need a glob, systemd
/// runs `ExecStartPre=` with no shell, and rules/security.md item 3 forbids
/// introducing one. The agent is also the only party that knows which half is
/// reproducible. So it moved here.
///
/// # What it does, in order
///
/// - Anything directly under the root that is not the `backup` directory:
///   removed. Nothing else has ever written there.
/// - Inside each `backup/<id>/`: everything except `rollback/` — the extracted
///   archive dumps.
/// - A `backup/<id>/` with no `rollback/` in it: removed entire.
/// - Surviving rollback sets past [`RETAINED_ROLLBACK_SETS`], oldest first.
/// - Every set that survives is logged by path and by the dumps in it, which is
///   the condition the audit put on deleting anything at all: an operator is
///   told which databases were left half-replaced and where their pre-restore
///   state is.
///
/// # It must not run while the daemon serves
///
/// There is no per-id lock to take — a scratch directory is named by a backup
/// id, and this function deliberately parses nothing out of a name. Its safety
/// is ordering and only ordering: its one caller is the line in
/// [`recover_restores`](crate::backup::recover_restores) that runs before
/// `server::serve` binds the socket, so no operation can be staging into the
/// tree it is emptying.
pub(crate) fn reap_backup_scratch(scratch_root: &Path, recovery: &mut RestoreRecovery) {
    let Ok(entries) = read_dir(scratch_root) else {
        return;
    };

    let mut area: Option<PathBuf> = None;
    for entry in entries.flatten() {
        if entry.file_name() == SCRATCH_AREA {
            area = Some(entry.path());
            continue;
        }
        remove_entry(&entry.path(), recovery);
    }

    let Some(area) = area else {
        return;
    };
    let Ok(operations) = read_dir(&area) else {
        return;
    };

    let mut kept: Vec<PathBuf> = Vec::new();
    for operation in operations.flatten() {
        match strip_to_rollback(&operation.path(), recovery) {
            Some(rollback) => kept.push(rollback),
            None => remove_entry(&operation.path(), recovery),
        }
    }

    retain_newest(kept, recovery);
}

/// Empties one operation's scratch of everything but its rollback dumps, and
/// answers where they are — or `None` because it had none.
///
/// A rollback directory that exists but is EMPTY answers `None` too, and is
/// removed with the rest: it is what a restore that was killed before its first
/// dump leaves, and keeping an empty directory under a retention cap would spend
/// one of the four slots on nothing.
fn strip_to_rollback(operation: &Path, recovery: &mut RestoreRecovery) -> Option<PathBuf> {
    let Ok(contents) = read_dir(operation) else {
        return None;
    };

    let mut rollback: Option<PathBuf> = None;
    for entry in contents.flatten() {
        if entry.file_name() == ROLLBACK_DIRECTORY && entry.path().is_dir() {
            rollback = Some(entry.path());
            continue;
        }
        remove_entry(&entry.path(), recovery);
    }

    let rollback = rollback?;
    let dumps = dump_names(&rollback);
    if dumps.is_empty() {
        return None;
    }

    tracing::warn!(
        path = %rollback.display(),
        dumps = %dumps.join(", "),
        "a killed restore left pre-restore database dumps behind; they are the ONLY copy of \
         those databases' state before the restore dropped them, and they were kept"
    );
    Some(rollback)
}

/// The file names inside a rollback directory, sorted, for the log line.
///
/// Names only. A dump's CONTENTS are a customer's rows and never reach a log at
/// any level (rules/rust.md "Logging").
fn dump_names(rollback: &Path) -> Vec<String> {
    let Ok(entries) = read_dir(rollback) else {
        return Vec::new();
    };

    let mut names: Vec<String> = entries
        .flatten()
        .map(|entry| entry.file_name().to_string_lossy().into_owned())
        .collect();
    names.sort();
    names
}

/// Removes all but the newest [`RETAINED_ROLLBACK_SETS`] rollback directories.
///
/// Newest by modification time, which is a comparison between two files that
/// both exist rather than a reading of the current time — so this stays out of
/// the ambient-clock ban and stays deterministic in a test that creates the
/// directories in a known order.
///
/// A directory whose time cannot be read sorts as the oldest, so an unreadable
/// entry is a candidate for removal rather than something that occupies a
/// retention slot forever.
fn retain_newest(mut sets: Vec<PathBuf>, recovery: &mut RestoreRecovery) {
    sets.sort_by_key(|path| {
        path.symlink_metadata()
            .and_then(|metadata| metadata.modified())
            .unwrap_or(SystemTime::UNIX_EPOCH)
    });

    while sets.len() > RETAINED_ROLLBACK_SETS {
        // `remove(0)` on a vector this short is cheaper to read than a reversed
        // sort, and the vector holds at most one entry per killed restore.
        let oldest = sets.remove(0);
        tracing::warn!(
            path = %oldest.display(),
            "more killed restores are on this host than the retention keeps; the OLDEST set of \
             pre-restore database dumps was removed"
        );
        remove_entry(&oldest, recovery);
    }

    recovery.rollback_sets_kept = u32::try_from(sets.len()).unwrap_or(u32::MAX);
}

/// Removes one entry, directory or not, and counts it.
///
/// Best effort by design: a scratch entry that will not go is disk to reclaim
/// and a line in the journal, never a reason for a root daemon to refuse to
/// start.
fn remove_entry(path: &Path, recovery: &mut RestoreRecovery) {
    let removed = if path.is_dir() {
        remove_dir_all(path).is_ok()
    } else {
        remove_file(path).is_ok()
    };

    if removed {
        recovery.scratch_entries_removed = recovery.scratch_entries_removed.saturating_add(1);
    } else {
        tracing::warn!(
            path = %path.display(),
            "a bulk scratch entry could not be removed at startup"
        );
    }
}

#[cfg(test)]
#[path = "../tests/backup/reap_backup_scratch_tests.rs"]
mod tests;
