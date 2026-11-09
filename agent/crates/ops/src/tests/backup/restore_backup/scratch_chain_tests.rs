//! The scratch chain a restore stages in, and the refusals of a chain that is
//! not sound.

use std::fs::{DirBuilder, Permissions, set_permissions};
use std::os::unix::fs::{MetadataExt as _, PermissionsExt as _, symlink};
use std::path::PathBuf;

use maran_agent_core::utils::current_uid::current_uid;
use tempfile::TempDir;

use crate::backup::open_restore_scratch::open_restore_scratch;

use super::super::*;

/// A chain a restore may stage in, built by the test and handed to
/// [`open_restore_scratch`] exactly as [`restore_backup`] hands it the real one.
///
/// The uid is the test's own rather than root's: no test can create a
/// root-owned directory, and a gate exercised only on input it must reject
/// passes just as well once it has been mutated into rejecting everything
/// (rules/testing.md). Every refusal below therefore has an accepting twin.
///
/// The chain rule itself is one function shared with the creation side and is
/// pinned level by level in `create_backup_tests.rs`. What these four ask is
/// the question that suite cannot answer: whether a RESTORE runs it at all —
/// which it did not, for as long as this file had no such test, while the
/// creation side's suite stayed green.
struct RestoreChain {
    /// The temporary directory every level lives under. `0700`, so it stands
    /// in for a `/var/lib` nobody but root can write.
    base: TempDir,
}

impl RestoreChain {
    /// A base with the mode a real ancestor must have.
    fn new() -> Self {
        let base = TempDir::new().unwrap();
        set_permissions(base.path(), Permissions::from_mode(0o700)).unwrap();

        Self { base }
    }

    /// Creates a directory under the base with an explicit mode.
    fn make(&self, relative: &str, mode: u32) -> PathBuf {
        let path = self.base.path().join(relative);
        DirBuilder::new().recursive(true).create(&path).unwrap();
        set_permissions(&path, Permissions::from_mode(mode)).unwrap();

        path
    }

    /// Runs the gate over `root`, staging `<root>/backup/an-id`.
    fn open(&self, root: &Path) -> Result<PathBuf, BackupError> {
        open_restore_scratch(
            self.base.path(),
            root,
            &root.join("backup").join("an-id"),
            current_uid().unwrap(),
        )
    }
}

/// **The accepting control for the three refusals below.** A sound chain is
/// used and the scratch comes back `0700`.
///
/// Without it, all three are satisfied by a gate that refuses unconditionally —
/// a restore that can never run again.
#[test]
fn a_restore_scratch_chain_that_is_sound_at_every_level_is_accepted() {
    let chain = RestoreChain::new();
    let root = chain.make("maran-scratch", 0o700);

    let scratch = chain.open(&root).expect("a sound chain is usable");

    assert_eq!(scratch, root.join("backup").join("an-id"));
    assert_eq!(scratch.symlink_metadata().unwrap().mode() & 0o7777, 0o700);
}

/// The measured escalation, against the restore: an unprivileged uid owning a
/// directory ABOVE the scratch is all it needs to rename the level below aside
/// and leave a symlink at that name. A restore extracts the archive's database
/// dumps into this directory as root and then loads them, so what that buys is
/// a plaintext copy of every database in the backup AND a say in what goes back
/// into the live one.
#[test]
fn a_writable_directory_above_the_restore_scratch_root_is_refused() {
    let chain = RestoreChain::new();
    chain.make("state", 0o777);
    let root = chain.make("state/maran-scratch", 0o700);

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}

/// A symlink at the scratch root's own name is not a directory, whatever it
/// points at — and nothing is created through it.
#[test]
fn a_symlink_standing_in_for_the_restore_scratch_root_is_refused() {
    let chain = RestoreChain::new();
    let target = chain.make("elsewhere", 0o700);
    let root = chain.base.path().join("maran-scratch");
    symlink(&target, &root).unwrap();

    let refusal = chain.open(&root);

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
    assert!(
        !target.join("backup").exists(),
        "nothing was created through the link"
    );
}

/// A chain sound in every other way but belonging to somebody other than the
/// uid the restore expects is refused.
///
/// The expected owner is root on a real host and no test can create a
/// root-owned directory, so this asks it the other way round: a chain the test
/// really owns, checked against an owner it is not.
#[test]
fn a_restore_scratch_chain_owned_by_somebody_else_is_refused() {
    let chain = RestoreChain::new();
    let root = chain.make("maran-scratch", 0o700);
    let owner = current_uid().unwrap();

    let refusal = open_restore_scratch(
        chain.base.path(),
        &root,
        &root.join("backup").join("an-id"),
        owner + 1,
    );

    assert!(matches!(refusal, Err(BackupError::ScratchUnusable)));
}
