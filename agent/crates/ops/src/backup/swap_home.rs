//! Step 7 of a restore: the two renames that put the restored home in place.

use std::fs::rename;

use maran_agent_core::validation::system::name::AccountName;

use crate::backup::backup_error::BackupError;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::restore_placement::RestorePlacement;

/// Step 7: the two renames.
///
/// The order is fixed and each rename is a different kind of risk. The home is
/// moved ASIDE first and the staging tree moved in second, so that the moment
/// of danger is a window between two renames on one filesystem rather than a
/// destructive delete. `rename` on one filesystem is a directory-entry
/// operation: it does not copy the customer's twenty gigabytes, and it either
/// happened or it did not.
///
/// Reversed — staging in first, then the old home aside — there is no window at
/// all, because the first rename would have to overwrite a non-empty directory,
/// which `rename` refuses, and the restore could never complete. The order is
/// the only one that works, and the window it costs is measured in
/// microseconds.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] when the home cannot be moved aside at all
/// — at that point nothing has changed — and [`BackupError::HomeParkedAt`] when
/// the second rename failed AND reversing the first failed too, carrying the
/// path an operator has to move it back from.
pub(super) fn swap_home(
    placement: &RestorePlacement,
    account: &AccountName,
    sink: &mut dyn RestoreSink,
) -> Result<(), BackupError> {
    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.start_percent(),
    );

    let home = placement.home_root.join(account.as_str());
    rename(&home, &placement.previous).map_err(|_| BackupError::StagingUnusable)?;

    // The one report that falls BETWEEN the two renames, and the only point in
    // the whole operation at which the account has no home. It is reported for
    // the reason every other stage boundary is: a fixed span is what makes a
    // stall legible, and an operation stuck at the start of `restoring_files`
    // and one stuck between the renames are two very different states for an
    // operator to walk into — the second one has a home parked under
    // `RESTORE_STAGING_ROOT`. `percent_through` rather than a literal, so no
    // call site writes a percentage down (see `RestoreStage`).
    //
    // It is also the seam the polygon suite kills the process through, which is
    // stated rather than left for a reader to discover: a test that has to
    // interrupt this window needs a real moment inside it, and this is the
    // agent's own report rather than a hook added for a test.
    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.percent_through(1, 2),
    );

    if rename(&placement.staging, &home).is_err() {
        // Immediately, and before anything else is attempted: every microsecond
        // here is a microsecond in which the account has no home.
        return match rename(&placement.previous, &home) {
            Ok(()) => Err(BackupError::StagingUnusable),
            Err(_) => Err(BackupError::HomeParkedAt {
                path: placement.previous.to_string_lossy().into_owned(),
            }),
        };
    }

    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.end_percent(),
    );
    Ok(())
}
