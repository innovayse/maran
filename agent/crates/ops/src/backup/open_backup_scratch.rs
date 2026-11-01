//! The root-only scratch one backup creation stages its dumps and manifest in.

use std::fs::{DirBuilder, remove_dir_all};
use std::os::unix::fs::DirBuilderExt as _;
use std::path::{Path, PathBuf};

use crate::backup::backup_error::BackupError;
use crate::backup::root_only_chain::{MissingLevels, SCRATCH_MODE, require_root_only_chain};

/// The scratch's directory of dumps — the archive's `databases/` member.
const DATABASES_DIRECTORY: &str = "databases";

/// Creates the root-only scratch and its `databases/` directory, and answers
/// the latter — after proving, against the inodes, that the place it is about
/// to create them in is root's alone all the way up.
///
/// Anything left by a crashed earlier run under this id is removed first: the
/// id is this agent's own and unique to the operation, so what is there can
/// only be its own litter — and a stale dump left in place would be archived as
/// if it belonged to this backup.
///
/// # Why the chain is checked and not just the leaf
///
/// The modes this creates are `0700 root:root` at every level, and they were
/// measured to hold on a chain this code built end to end. They are still not a
/// boundary while somebody else owns an ancestor: the owner of a directory can
/// rename an entry inside it aside and put a symlink in its place *without ever
/// having permission to enter it*, and the next write from root then follows
/// the link. That was measured both ways round — a customer's plaintext dump
/// delivered into an attacker-readable file, and a root-owned `0600` file
/// truncated and overwritten — in
/// `docs/superpowers/notes/2026-09-05-backups-threat-note.md` §1, back when
/// `AgentPaths::BULK_SCRATCH_ROOT` lived inside `/var/lib/maran`, which the
/// panel's unprivileged service account owns (`maran:maran 0750` today; the
/// account was called `panel` when that was measured, and naming it by its old
/// spelling here would send a reader looking for an account no installed host
/// has).
///
/// Moving the root out of that tree is the primary fix and this check is the
/// second one, for the same reason
/// [`prepare_account_directory`](crate::backup::backup_root) states a mode it
/// just created: a location nobody can reach today is out of reach only for as
/// long as an installer, an operator or a later version keeps it that way, and
/// a mode that is assumed reports nothing when it stops being true. The check
/// runs against the path the dumps are actually written into, not against the
/// constant they were derived from.
///
/// # Errors
///
/// [`BackupError::ScratchUnusable`] — for a chain that could not be created,
/// and for one this agent refuses to write into. The two are one variant
/// because the answer to both is the same: nothing was staged, and the
/// operation stops before a single dump exists.
///
/// # Why the base, the root and the owner are parameters
///
/// For the reason `backup_root.rs` splits its own checks: a test does not run
/// as uid 0 and cannot create a directory owned by root, so a gate that
/// hard-coded root could only ever be exercised on its refusing path — and a
/// gate that has only ever been handed input it must reject passes just as
/// well once it has been mutated into rejecting everything (rules/testing.md,
/// "a refusing gate needs an inverse control"). Injected, a test can hand it a
/// chain it must ACCEPT and a chain it must refuse, and the refusal it
/// observes is the code the daemon runs. `create_backup` fills all three
/// from `AgentPaths` and root's uid; nothing in them comes from a request.
pub(super) fn open_backup_scratch(
    base: &Path,
    root: &Path,
    scratch: &Path,
    owner: u32,
) -> Result<PathBuf, BackupError> {
    // Before anything is created: what already exists on the way down must
    // already be root's. A level that does not exist yet is passed over here
    // and stated again below, once this call has made it.
    require_root_only_chain(base, root, scratch, owner, MissingLevels::Tolerated)?;

    let _ = remove_dir_all(scratch);

    let databases = scratch.join(DATABASES_DIRECTORY);
    DirBuilder::new()
        .recursive(true)
        .mode(SCRATCH_MODE)
        .create(&databases)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // And again on the finished chain, including the directory the dumps go in:
    // the same TOCTOU re-assertion `open_restore_scratch` makes, for the
    // same reason and with the same blind spot, and worded the same way so the
    // twins cannot drift apart.
    //
    // It is not, as this comment used to claim, a check on a directory this call
    // did not create: such a directory is already refused by the arm above, and
    // `databases` here was made by the `DirBuilder` two lines up with
    // `SCRATCH_MODE`, which a umask can only tighten. What is left is the window
    // between the check above and that `create` — a level renamed aside and
    // replaced inside it.
    //
    // That window is a race, so NO TEST OBSERVES THIS ARM (ledger S7: the
    // mutation removing it survives the whole suite). Kept, not deleted:
    // rules/testing.md deletes a defensive call that CANNOT FAIL, and this one
    // can — what cannot happen is the test.
    require_root_only_chain(base, root, &databases, owner, MissingLevels::Refused)?;

    Ok(databases)
}
