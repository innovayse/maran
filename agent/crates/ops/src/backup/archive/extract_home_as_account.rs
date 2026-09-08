//! Building the replacement home, as the account rather than as root.

use std::fs::{DirBuilder, Permissions, remove_dir_all, set_permissions};
use std::os::unix::fs::{DirBuilderExt as _, PermissionsExt as _, chown};
use std::path::Path;

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::archive_part::ArchivePart;
use crate::backup::model::extract_spec::ExtractSpec;

/// The mode the staging directory is created with before it changes hands.
///
/// `0700`, so that between the `mkdir` and the `chown` the directory is root's
/// alone — there is no instant at which it is a world-writable name somebody
/// else could win a race for. After the `chown` the same bits mean the account
/// alone, which is what the home it becomes must be.
const STAGING_MODE: u32 = 0o700;

/// The mode the restore staging ROOT must carry.
///
/// `0711`: traversable by everybody, listable by nobody but root. Both halves
/// are load-bearing.
///
/// Traversable, because the account's own `tar` runs INSIDE this root — the
/// staging directory it extracts into is a child of it — and a process that
/// cannot traverse a parent cannot open anything beneath it, whatever the
/// child's own mode says. `0700` root-owned here means every restore fails with
/// a permission error on a directory the account owns, which reads as anything
/// but the truth.
///
/// Not listable, because the entries under it are `<account>.<backup id>`: a
/// readable root would tell any customer on the host which of their neighbours
/// is being restored and from which backup. `0755` would be traversable too and
/// would give that away for nothing.
///
/// The root holds no data of its own. Every directory inside it is created
/// `0700` and handed to exactly one account, so "traversable by everybody"
/// grants the ability to reach a name and nothing else.
const STAGING_ROOT_MODE: u32 = 0o711;

/// Creates the restore's staging directory and extracts the archive's `home/`
/// into it AS the account.
///
/// # Why the account and not root (R3)
///
/// A restore unpacks an archive that a customer may have supplied, and
/// extraction is the moment an archive gets to choose where bytes go. Running
/// it as root means every mistake — in `tar`, in this argv, in the pre-scan —
/// is a root write at a path an attacker named. Running it as the account means
/// the worst case is the account writing somewhere the account could already
/// write, which is not "safe" but is a class of problem this product already
/// lives with rather than a new one.
///
/// The asymmetry with the create side is deliberate and both halves are argued
/// where they happen: creation reads the home as root because
/// `fork_as_account`'s child cannot hand an archive back — it closes every
/// inherited descriptor and reports an exit status and nothing else — while
/// extraction returns only success or failure, which is exactly what an exit
/// status carries. The asymmetry is not an oversight; it is the shape of the
/// only channel out of the child.
///
/// # The staging ROOT is repaired, not merely created
///
/// The root that holds the staging directories is created if it is missing and
/// **re-moded if it is there and wrong**, every time. That is not belt and
/// braces: `DirBuilder` applies its mode only to directories it CREATES, so a
/// host that has already run one restore under the earlier code has this root
/// sitting at `0700` root-owned forever, and a fix that only created it
/// correctly would leave every such host broken while looking correct in a
/// fresh container. Repair is the difference between fixing the code and fixing
/// the hosts.
///
/// The installer creates it too, beside the backup root
/// (`installer/lib/40-user.sh`), which is where an agent-owned directory
/// belongs — it exists before the first restore rather than at the moment one
/// needs it. The repair here is what covers hosts installed before that line and
/// hosts that already ran a restore; the two are not alternatives, and the
/// cheaper one alone is the one that leaves a broken host broken.
///
/// # The directory is made by root, and then handed over
///
/// The staging directory is created here, `0700`, and `chown`ed to the account
/// before anything drops privilege. Two reasons it is not created by the child:
/// a directory made by an unprivileged process under the restore staging root
/// needs that root to be writable by it, and it is root's alone on purpose; and
/// work that can be done in the parent should be, because the smallest child is
/// the best child (`fork_as_account`'s own contract).
///
/// `uid` and `gid` are passed in rather than resolved here. The operation has
/// already resolved the account — it needs the same uid to re-own the home root
/// at the end — and resolving twice would be two lookups that can disagree
/// across a `usermod` between them. It also lets a test, which runs as an
/// ordinary user and cannot `chown` anything to another account, exercise the
/// accepting path at all: a gate fed nothing but input it must reject passes
/// just as well once it has been mutated to reject everything
/// (rules/testing.md, "a refusing gate needs an inverse control").
///
/// Anything already at the staging path is removed first. The path carries this
/// operation's own backup id, so what is there can only be the litter of a
/// crashed earlier attempt at this same restore — and a stale tree left in
/// place would be swapped into the account's home as if this run had built it.
///
/// # Errors
///
/// - [`BackupError::StagingUnusable`] when the staging root is not a directory,
///   when its mode cannot be repaired, or when the staging directory itself
///   cannot be made or handed over.
/// - [`BackupError::ArchiveFailed`] when the archiver refuses, and
///   [`BackupError::ExtractionIdentityUnavailable`] when the drop to the
///   account fails — never a fall back to root, because an extraction that runs
///   as root because dropping did not work is the failure this exists to
///   prevent.
pub(crate) fn extract_home_as_account(
    host: &dyn BackupHost,
    artifact: &Path,
    staging: &Path,
    account: &AccountName,
    uid: u32,
    gid: u32,
) -> Result<(), BackupError> {
    let _ = remove_dir_all(staging);

    if let Some(root) = staging.parent() {
        repair_staging_root(root)?;
    }

    // NOT recursive: a recursive builder applies this `0700` to every parent it
    // has to create as well, which is exactly how the root above came to be
    // root-only. The root is made traversable on the line before, deliberately
    // and with its own mode.
    DirBuilder::new()
        .mode(STAGING_MODE)
        .create(staging)
        .map_err(|_| BackupError::StagingUnusable)?;
    chown(staging, Some(uid), Some(gid)).map_err(|_| BackupError::StagingUnusable)?;

    host.extract(&ExtractSpec {
        artifact: artifact.to_path_buf(),
        into: staging.to_path_buf(),
        // The identity this runs under is DERIVED from this part, not passed
        // beside it — see `ExtractSpec::identity`.
        part: ArchivePart::Home {
            account: account.clone(),
        },
    })
}

/// Makes sure `root` exists and carries [`STAGING_ROOT_MODE`], creating it or
/// re-moding it as it finds it.
///
/// Both branches, because the two situations are different hosts: a host that
/// has never restored needs the directory made, and a host that restored under
/// the earlier code has it already, at `0700`, where no `DirBuilder` will ever
/// touch it again.
///
/// A `root` that exists and is not a directory is refused rather than replaced.
/// It sits under the home root, which only root may write, so something being
/// there that is not a directory is an operator's doing or a defect — and
/// removing it to make room would be this function destroying a thing it does
/// not understand. `symlink_metadata` so that a symlink counts as "not a
/// directory" rather than as whatever it points at.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] in every case: this is one step of making
/// the staging tree, and a caller can do nothing different for a mode it could
/// not set than for a directory it could not create.
fn repair_staging_root(root: &Path) -> Result<(), BackupError> {
    match root.symlink_metadata() {
        Ok(metadata) if metadata.is_dir() => {
            if metadata.permissions().mode() & 0o7777 == STAGING_ROOT_MODE {
                return Ok(());
            }

            set_permissions(root, Permissions::from_mode(STAGING_ROOT_MODE))
                .map_err(|_| BackupError::StagingUnusable)
        }
        Ok(_) => Err(BackupError::StagingUnusable),
        Err(_) => DirBuilder::new()
            .recursive(true)
            .mode(STAGING_ROOT_MODE)
            .create(root)
            .map_err(|_| BackupError::StagingUnusable),
    }
}

#[cfg(test)]
#[path = "../../tests/backup/archive/extract_home_as_account_tests.rs"]
mod tests;
