//! Steps 1 to 5 of a restore: everything whose failure leaves the account as it was.

use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::extract_databases_as_root::extract_databases_as_root;
use crate::backup::archive::extract_home_as_account::extract_home_as_account;
use crate::backup::archive::read_manifest::read_manifest;
use crate::backup::archive::scan_members::scan_members;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::open_restore_scratch::open_restore_scratch;
use crate::backup::require_sidecar_agrees::require_sidecar_agrees;
use crate::backup::resolve_databases::resolve_databases;
use crate::backup::restore_placement::RestorePlacement;
use crate::backup::restore_plan::RestorePlan;

/// Steps 1 to 5: everything whose failure leaves the account exactly as it was.
///
/// The stage boundary and the recoverability boundary are the SAME boundary,
/// which is why all of this is reported as `verifying` even though step 5
/// really does write a whole home to disk. Nothing it writes is anywhere the
/// account can see, and every one of these returns undoes itself by the two
/// `remove_dir_all`s the caller runs unconditionally. The first report that is
/// not `verifying` is the first report after which doing nothing is no longer a
/// recovery.
///
/// # Errors
///
/// As documented on [`restore_backup`](crate::backup::restore_backup), for steps 1 to 5.
#[allow(clippy::too_many_arguments)]
pub(super) fn prepare_restore(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    account: &AccountName,
    artifact: &Path,
    sidecar: &Path,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    sink: &mut dyn RestoreSink,
) -> Result<RestorePlan, BackupError> {
    sink.report(
        RestoreStage::Verifying,
        RestoreStage::Verifying.start_percent(),
    );

    if artifact.symlink_metadata().is_err() {
        return Err(BackupError::NotFound);
    }

    // Step 1. Before the archive is opened for any purpose: are these the bytes
    // the panel took? Everything after this point treats the archive as this
    // agent's own output, and that treatment has to be earned first.
    let (actual_sha256, _bytes) = checksum_file(artifact)?;
    if actual_sha256 != expected_sha256 {
        return Err(BackupError::ChecksumMismatch);
    }

    let scratch = open_restore_scratch(
        &placement.scratch_base,
        &placement.scratch_root,
        &placement.scratch,
        placement.scratch_owner,
    )?;

    // Step 2. Names only, and the whole archive judged before any of it is
    // unpacked.
    scan_members(host, artifact)?;

    // Step 3.
    let manifest = read_manifest(host, artifact, &scratch, account.as_str())?;
    require_sidecar_agrees(sidecar, &manifest, &actual_sha256)?;
    let databases = resolve_databases(&manifest, allowed_databases)?;

    // Step 4.
    extract_databases_as_root(host, artifact, &scratch, &databases)?;

    // Step 5.
    extract_home_as_account(
        host,
        artifact,
        &placement.staging,
        account,
        placement.owner,
        placement.account_group,
    )?;

    sink.report(
        RestoreStage::Verifying,
        RestoreStage::Verifying.end_percent(),
    );

    Ok(RestorePlan { databases })
}
