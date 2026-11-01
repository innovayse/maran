//! The root-only scratch one restore stages its extracted dumps in.

use std::fs::{DirBuilder, remove_dir_all};
use std::os::unix::fs::DirBuilderExt as _;
use std::path::{Path, PathBuf};

use crate::backup::backup_error::BackupError;
use crate::backup::root_only_chain::{MissingLevels, SCRATCH_MODE, require_root_only_chain};

/// Creates the root-only scratch this restore stages into, and answers it —
/// after proving, against the inodes, that the place it is about to create it
/// in is root's alone all the way up.
///
/// Anything left by a crashed earlier run under this id is removed first: the
/// id is this operation's own, so what is there can only be its own litter —
/// and a stale dump left in place would be loaded into a live database as if
/// this run had extracted it.
///
/// # Why the chain is checked and not just the leaf
///
/// The same argument [`create_backup`](crate::backup::create_backup) makes for
/// its own scratch, and it is if anything sharper here: a restore extracts the
/// archive's `databases/*` into this directory as ROOT and then loads them, so
/// an ancestor somebody else owns buys them both a plaintext copy of every
/// database in the backup and a say in what gets loaded back into the live
/// one. The two operations stage under the same root, so a boundary enforced
/// by one of them and not the other is not a boundary — the check itself lives
/// in [`root_only_chain`](crate::backup::root_only_chain) so there is exactly
/// one of it.
///
/// # Errors
///
/// [`BackupError::ScratchUnusable`] — for a chain that could not be created,
/// and for one this agent refuses to write into. The two are one variant
/// because the answer to both is the same: nothing was staged, and the restore
/// stops before the archive has been opened.
///
/// # Why the base, the root and the owner are parameters
///
/// So the accepting path can be exercised: a test does not run as uid 0 and
/// cannot create a directory owned by root, and a gate that has only ever been
/// handed input it must reject passes just as well once it has been mutated
/// into rejecting everything (rules/testing.md, "a refusing gate needs an
/// inverse control"). `restore_backup` fills all three from `AgentPaths`
/// and root's uid; nothing in them comes from a request.
pub(super) fn open_restore_scratch(
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

    DirBuilder::new()
        .recursive(true)
        .mode(SCRATCH_MODE)
        .create(scratch)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // And again on the finished chain: a TOCTOU re-assertion, and nothing else.
    //
    // Said precisely, because the reason written here before was measured to be
    // wrong. It claimed this arm catches a directory this call did not create —
    // one left by an older version, or chmodded since. It does not: such a
    // directory is already refused by the arm above, and what this call itself
    // created cannot be looser than `SCRATCH_MODE`, because a umask can only
    // take bits away. Every state this arm can refuse that the pre-arm cannot
    // is a state produced INSIDE the window between the two — a level renamed
    // aside and replaced between the check above and the `create` below.
    //
    // That window is a race, so NO TEST OBSERVES THIS ARM, in this repository
    // or in principle in-process: a mutation that deletes it survives the whole
    // suite (ledger S9). It is kept rather than deleted because rules/testing.md
    // deletes a defensive call that CANNOT FAIL, and this one can — what cannot
    // happen is the test. It is held by review, and a coverage report that
    // shows it unreached is reporting the truth rather than a gap to close.
    require_root_only_chain(base, root, scratch, owner, MissingLevels::Refused)?;

    Ok(scratch.to_path_buf())
}
