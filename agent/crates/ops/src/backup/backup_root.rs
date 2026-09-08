//! The check that asks the INODE what the configured backup root really is.

use std::fs::DirBuilder;
use std::os::unix::fs::{DirBuilderExt as _, MetadataExt as _};
use std::path::{Path, PathBuf};

use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;

/// The uid a backup directory must belong to on a real host.
const ROOT_UID: u32 = 0;

/// The mode every directory in the backup tree is created with, and the only
/// one it may carry: `rwx` for its owner and nothing at all for anybody else.
const ROOT_ONLY_MODE: u32 = 0o700;

/// The permission bits that must be clear: every group and other bit.
const GROUP_AND_OTHER_BITS: u32 = 0o077;

/// The bits that must also be clear: setuid, setgid and the sticky bit.
///
/// None of them belongs on a backup directory, and setgid in particular is
/// contagious — it is inherited by everything created underneath, so a setgid
/// backup root quietly hands a group ownership of every artifact written after
/// it.
const SPECIAL_BITS: u32 = 0o7000;

/// Prepares the account's directory under the configured backup root, and
/// refuses to write anything into it unless the filesystem itself says it is
/// root's alone.
///
/// # Why this exists at all, given that the root is a validated type
///
/// `LocalBackupRoot::parse` answered a question about a STRING: does the
/// operator's text name a place an archive may rest. It cannot answer who owns
/// that directory or what its mode is, because those are properties of an
/// inode, they have different answers at different moments, and a value parsed
/// at boot says nothing about a directory somebody chmodded at noon. So they
/// are asked HERE — separately, and immediately before the write, against the
/// path the write actually goes to.
///
/// The stakes are the reason the type refuses `/home` in the first place. A
/// backup directory anyone but root can write is a directory where the artifact
/// a later restore reads is an artifact somebody else chose, and that restore
/// extracts as root and loads SQL as the database superuser. Mode drift gets
/// there by a different road than a bad configuration string, and neither check
/// sees the other's road.
///
/// # What it refuses
///
/// Both the resolved root and the account's directory under it must be a real
/// directory (not a symlink to one), owned by root, with no group bit, no other
/// bit, and none of setuid/setgid/sticky. Anything else is
/// [`BackupError::BackupRootUnsafe`], and nothing is written.
///
/// A root that cannot be canonicalised, or a directory that cannot be created,
/// is [`BackupError::BackupRootUnusable`] — reported as unproven rather than as
/// safe: this check says what it could observe, and it observed nothing.
///
/// # Errors
///
/// As above.
pub(crate) fn prepare_account_directory(
    root: &LocalBackupRoot,
    account: &AccountName,
) -> Result<PathBuf, BackupError> {
    let resolved = root
        .resolve()
        .map_err(|_| BackupError::BackupRootUnsafe { uid: 0, mode: 0 })?;

    prepare_account_directory_owned_by(&resolved, account, ROOT_UID)
}

/// The body of [`prepare_account_directory`], with the expected owner injected.
///
/// Split for the reason `LocalBackupRoot`'s own resolver is split: a test runs
/// as an ordinary user and cannot create a directory owned by uid 0, so a check
/// that hard-coded root could only ever be exercised on its refusing path — and
/// a gate that has only ever been fed input it must reject passes just as well
/// when it has been mutated to reject everything (rules/testing.md, "a refusing
/// gate needs an inverse control"). With the uid injected, a test can hand it a
/// directory it must ACCEPT and one it must refuse.
///
/// # Errors
///
/// As documented on [`prepare_account_directory`].
fn prepare_account_directory_owned_by(
    root: &Path,
    account: &AccountName,
    owner: u32,
) -> Result<PathBuf, BackupError> {
    require_root_only(root, owner)?;

    let directory = root.join(account.as_str());
    if directory.symlink_metadata().is_err() {
        DirBuilder::new()
            .mode(ROOT_ONLY_MODE)
            .create(&directory)
            .map_err(|_| BackupError::BackupRootUnusable)?;
    }

    // Stated again after the creation, and against the account's own directory
    // rather than the root's: this is the path the artifact is written into, and
    // "the parent was fine" is not an answer about it. A directory left behind
    // by an earlier version, or chmodded since, reaches this line looking
    // exactly like one this call just made.
    require_root_only(&directory, owner)?;

    Ok(directory)
}

/// Opens the account's existing directory under the configured backup root,
/// answering `None` when the account has none — and creating nothing.
///
/// # Why a second entry point rather than a flag on the first
///
/// [`prepare_account_directory`] is named for what it does: it is the call a
/// WRITE makes, and creating the directory is the write's own first step. An
/// operation that only reads or removes has no business leaving an inode
/// behind, and an idempotent delete least of all — "delete an account that has
/// no backups" answering [`BackupError::NotFound`] *and* creating a directory
/// on the way out is a converged outcome that changed the filesystem. A
/// boolean parameter would express the same thing while letting a caller pass
/// the wrong one silently; two named functions cannot be got wrong.
///
/// The inode checks are the same ones and for the same reason: the directory
/// this answers is a directory a caller is about to unlink files from, and a
/// path that is a symlink, or is reachable by somebody other than root, is not
/// one this agent will act inside — reading a backup's name out of it is how
/// an attacker gets to choose which artifact a later operation touches.
///
/// # Errors
///
/// As documented on [`prepare_account_directory`], minus
/// [`BackupError::BackupRootUnusable`] for a creation that could not happen —
/// nothing is created here.
pub(crate) fn open_account_directory(
    root: &LocalBackupRoot,
    account: &AccountName,
) -> Result<Option<PathBuf>, BackupError> {
    let resolved = root
        .resolve()
        .map_err(|_| BackupError::BackupRootUnsafe { uid: 0, mode: 0 })?;

    open_account_directory_owned_by(&resolved, account, ROOT_UID)
}

/// The body of [`open_account_directory`], with the expected owner injected —
/// split for the reason [`prepare_account_directory_owned_by`] is split: a
/// test does not run as root and could otherwise exercise only the refusing
/// path.
///
/// `pub(crate)` rather than private, for the same reason the split exists:
/// [`list_backups`](crate::backup::list_backups) is a read whose whole
/// contract is that it creates nothing, and a test can only observe that on a
/// directory it owns itself. Without this, "the listing calls the opening entry
/// point and not the preparing one" is a fact no test could see, and the
/// operation could be changed back with the suite green.
///
/// # Errors
///
/// As documented on [`open_account_directory`].
pub(crate) fn open_account_directory_owned_by(
    root: &Path,
    account: &AccountName,
    owner: u32,
) -> Result<Option<PathBuf>, BackupError> {
    require_root_only(root, owner)?;

    let directory = root.join(account.as_str());
    if directory.symlink_metadata().is_err() {
        return Ok(None);
    }

    require_root_only(&directory, owner)?;

    Ok(Some(directory))
}

/// Asserts that `path` is a directory belonging to `owner` and reachable by
/// nobody else.
///
/// `symlink_metadata` and not `metadata`: the question is what this path IS,
/// and `metadata` follows a symlink, so a link pointing at `/root` would be
/// approved on the strength of the target's ownership while the write went
/// wherever the link's author later re-pointed it.
///
/// # Errors
///
/// [`BackupError::BackupRootUnsafe`] carrying what was found, or
/// [`BackupError::BackupRootUnusable`] when the path could not be stated.
fn require_root_only(path: &Path, owner: u32) -> Result<(), BackupError> {
    let metadata = path
        .symlink_metadata()
        .map_err(|_| BackupError::BackupRootUnusable)?;

    if !metadata.is_dir() {
        return Err(BackupError::BackupRootUnsafe {
            uid: metadata.uid(),
            mode: 0,
        });
    }

    let mode = metadata.mode() & 0o7777;
    if metadata.uid() != owner || mode & (GROUP_AND_OTHER_BITS | SPECIAL_BITS) != 0 {
        return Err(BackupError::BackupRootUnsafe {
            uid: metadata.uid(),
            mode,
        });
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/backup_root_tests.rs"]
mod tests;
