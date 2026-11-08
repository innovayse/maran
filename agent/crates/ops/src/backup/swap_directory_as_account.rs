//! The two renames that put one restored directory in place of the live one.

use std::ffi::OsStr;
use std::io;

use maran_agent_core::privs::rename_no_replace_between_directories::rename_no_replace_between_directories;
use maran_agent_core::privs::stat_in_directory::stat_in_directory;

use crate::backup::backup_error::BackupError;
use crate::backup::model::directory_swap::DirectorySwap;
use crate::backup::open_restore_directory::{open_owned_directory, open_parent_of};

/// The name the displaced tree is given inside the parking directory.
pub(crate) const PARKED_ENTRY: &str = "previous";

/// Replaces the live directory `swap.directory` with the tree extracted into
/// `swap.staging`, by renames between directories held open.
///
/// **Runs inside `fork_as_account`**, as the account: the displaced tree and the
/// restored one are both the account's own, and every rename below is something
/// the account could already do to its own home. Nothing here follows a link at
/// any component, and nothing is copied: the three directories (the live
/// directory's parent, the staging directory and the parking directory) are
/// opened once and the entries move between them by `renameat2(RENAME_NOREPLACE)`
/// — a directory-entry operation on one filesystem that either happened or did
/// not. A different filesystem is `EXDEV` and a failed restore, never a copy.
///
/// The order is the same as the whole-home swap's and for the same reason: the
/// live directory is moved ASIDE first and the restored one moved in second, so
/// the moment of danger is a window of microseconds between two renames, not a
/// destructive delete. If the second rename fails the first is reversed at once.
///
/// The live directory not being there is not an error: a site whose directory the
/// customer deleted is exactly what a restore is for, and then there is nothing
/// to park.
///
/// # Errors
///
/// - [`BackupError::DirectoryUnusable`] when the walk to the live directory's
///   parent, or either working directory, is refused.
/// - [`BackupError::DirectoryNotInBackup`] when the extraction left no entry of
///   the expected name in `swap.staging` (nothing has moved).
/// - [`BackupError::StagingUnusable`] when the live directory cannot be moved
///   aside or the restored one cannot be moved in and the reversal succeeded —
///   the live directory is back where it was.
/// - [`BackupError::DirectoryParkedAt`] when the second rename and the reversal
///   both failed; the displaced tree is in the parking directory.
pub(crate) fn swap_directory_as_account(swap: &DirectorySwap, uid: u32) -> Result<(), BackupError> {
    let parent = open_parent_of(&swap.home, &swap.directory, uid)?;
    let staging = open_owned_directory(&swap.staging, uid)?;
    let parked = open_owned_directory(&swap.parked, uid)?;

    let name = OsStr::new(swap.directory.file_name());
    let displaced = OsStr::new(PARKED_ENTRY);

    // The restored tree must be there before anything is moved aside: moving the
    // live directory away and then finding nothing to put in its place would turn
    // a refusal into an outage.
    match stat_in_directory(&staging, name) {
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Err(BackupError::DirectoryNotInBackup);
        }
        Err(_) => return Err(BackupError::DirectoryUnusable),
    }

    let was_parked = match stat_in_directory(&parent, name) {
        Ok(_) => {
            rename_no_replace_between_directories(&parent, name, &parked, displaced)
                .map_err(|_| BackupError::StagingUnusable)?;
            true
        }
        Err(error) if error.kind() == io::ErrorKind::NotFound => false,
        Err(_) => return Err(BackupError::DirectoryUnusable),
    };

    if rename_no_replace_between_directories(&staging, name, &parent, name).is_err() {
        // Immediately, and before anything else is attempted: every microsecond
        // here is one in which the site has no directory.
        if !was_parked {
            return Err(BackupError::StagingUnusable);
        }

        return match rename_no_replace_between_directories(&parked, displaced, &parent, name) {
            Ok(()) => Err(BackupError::StagingUnusable),
            Err(_) => Err(BackupError::DirectoryParkedAt {
                path: swap
                    .parked
                    .join(PARKED_ENTRY)
                    .to_string_lossy()
                    .into_owned(),
            }),
        };
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/swap_directory_as_account_tests.rs"]
mod tests;
