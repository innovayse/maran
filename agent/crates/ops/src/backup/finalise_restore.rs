//! Step 8 of a restore: the home root's ownership and mode, and the parked tree.

use std::fs::remove_dir_all;
use std::os::unix::fs::{PermissionsExt as _, chown};

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::restore_placement::RestorePlacement;

/// The mode the account's home directory carries.
pub(super) const HOME_MODE: u32 = 0o750;

/// Step 8: the home root's ownership and mode, and the parked tree.
///
/// **Measured, not assumed.** An account's home is created owned by the account
/// but group-owned by the WEB SERVER's group, mode `0750`, because the web
/// server has to traverse the home to serve the site. A restore that put the
/// account's own group back would break every site the account owns —
/// silently, with a 403 from nginx and no error anywhere in this operation. So
/// the group is re-applied here from the value the caller resolved out of the
/// distro adapter, and it is re-applied BEFORE the parked tree is removed, so
/// that a failure at this step is one an operator can still undo by hand.
///
/// Only the home ROOT is touched. Everything inside it was written by the
/// account, by construction, because the extraction ran as the account.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] when the ownership or mode cannot be
/// applied. The removal of the parked tree is NOT an error: at this point the
/// restore has succeeded, and a leftover directory is disk space to reclaim,
/// not a customer who cannot work.
pub(super) fn finalise_restore(
    placement: &RestorePlacement,
    account: &AccountName,
    sink: &mut dyn RestoreSink,
) -> Result<(), BackupError> {
    sink.report(
        RestoreStage::Finalising,
        RestoreStage::Finalising.start_percent(),
    );

    let home = placement.home_root.join(account.as_str());
    chown(&home, Some(placement.owner), Some(placement.group))
        .map_err(|_| BackupError::StagingUnusable)?;
    std::fs::set_permissions(&home, std::fs::Permissions::from_mode(HOME_MODE))
        .map_err(|_| BackupError::StagingUnusable)?;

    let _ = remove_dir_all(&placement.previous);

    sink.report(
        RestoreStage::Finalising,
        RestoreStage::Finalising.end_percent(),
    );
    Ok(())
}
