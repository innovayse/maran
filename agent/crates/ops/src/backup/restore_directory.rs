//! The irreversible half of a single-directory restore.

use std::fs::remove_dir_all;

use maran_agent_core::validation::fs::relative_path::RelativePath;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::archive::extract_home_as_account::make_staging_directory;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::directory_swap::DirectorySwap;
use crate::backup::model::restore_outcome::RestoreOutcome;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::restore_placement::RestorePlacement;

/// Replaces `directory` of the account's home with the tree already extracted
/// into the staging directory, and nothing else (issue #160).
///
/// # If this fails, what state is the site in?
///
/// | Step | If it fails HERE, the directory is |
/// |---|---|
/// | the account is asked again who it is | Untouched. A uid that changed since the restore began is refused before anything moves. |
/// | the parking directory is made | Untouched. |
/// | the live directory is moved into the parking directory | Untouched: the rename did not happen. |
/// | the restored tree is moved into place | Put back by an immediate reverse rename, and the error says nothing was changed. If the reverse rename fails too, the error names the parking path, and **that directory is left in place** — it holds the only copy of the site as it was. |
///
/// There is no point-of-no-return step the way the database drop is one: both
/// renames are directory-entry operations on one filesystem, and the old tree is
/// not deleted until both have succeeded.
///
/// # What is not covered
///
/// No restore marker is written. The whole-home swap writes one so that a
/// process which never saw the request can finish a swap a killed one started;
/// this swap has no such recovery. A kill between its two renames leaves the
/// directory missing and the old tree in the parking directory, which the
/// startup reconciliation deliberately leaves alone (an unmarked entry in the
/// staging root is left for an operator). That window is microseconds wide and
/// the customer's net is the pre-restore backup the panel took; both are stated
/// in the threat note rather than hidden.
///
/// # Errors
///
/// [`BackupError::AccountIdentityChanged`], [`BackupError::StagingUnusable`] and
/// [`BackupError::DirectoryParkedAt`], as above, and whatever
/// [`BackupHost::swap_directory`] documents.
pub(super) fn restore_directory(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    directory: &RelativePath,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    // Asked again for the reason `perform` asks it: the uid in `placement` was
    // read when the operation began, and the staging tree was handed to it.
    let current = host.account_identity(account)?;
    if current.uid != placement.owner || current.gid != placement.account_group {
        return Err(BackupError::AccountIdentityChanged);
    }

    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.start_percent(),
    );

    // The parking directory is a sibling of the staging directory and not inside
    // it: the cleanup that removes the staging tree on every path must never be
    // able to delete the only copy of the displaced site.
    make_staging_directory(
        &placement.previous,
        placement.owner,
        placement.account_group,
    )?;

    let swap = DirectorySwap {
        home: placement.home_root.join(account.as_str()),
        staging: placement.staging.clone(),
        parked: placement.previous.clone(),
        directory: directory.clone(),
    };
    let swapped = host.swap_directory(account, &swap);

    // Everything but a tree that is still parked is litter. A parked tree is the
    // customer's site, and it stays where the error says it is.
    if !matches!(swapped, Err(BackupError::DirectoryParkedAt { .. })) {
        let _ = remove_dir_all(&placement.previous);
    }
    swapped?;

    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.end_percent(),
    );

    Ok(RestoreOutcome {
        files_restored: true,
        databases_restored: 0,
        databases_total: 0,
    })
}
