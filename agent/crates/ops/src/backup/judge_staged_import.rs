//! Judging a staged import's archive against the manifest that was signed for it.

use std::collections::BTreeSet;
use std::path::Path;

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::archive::is_encrypted_artifact::is_encrypted_artifact;
use crate::backup::archive::read_manifest::read_manifest;
use crate::backup::archive::scan_members::scan_members;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::import_scratch::ImportScratch;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::open_restore_scratch::open_restore_scratch;
use crate::backup::package::transfer_manifest::TransferManifest;
use crate::backup::verify_backup::require_dumps_present;

/// Applies to the staged file at `staged` the checks a restore would apply to it
/// later, plus the comparison with what the sender signed, and answers the archive's
/// own manifest.
///
/// In order: the file is not a sealed archive; every member is inside the fixed
/// layout (`scan_members`, the pre-scan a restore runs); the archive's manifest,
/// extracted into the root-only scratch, parses, is of a known version and names
/// `account`; it names `backup_id`; the databases it lists are exactly the ones the
/// signed manifest lists; and every one of them has its dump among the members. The
/// signed manifest plays the sidecar's part here: it is the record the artifact is
/// judged against, and a disagreement is reported as
/// [`BackupError::ManifestDisagreesWithSidecar`].
///
/// Nothing of the archive is extracted but the manifest; no dump is read or loaded.
///
/// # Errors
///
/// - [`BackupError::SealedArtifactNotTransferable`] for a sealed archive.
/// - [`BackupError::UnexpectedArchiveMember`], [`BackupError::ManifestUnreadable`],
///   [`BackupError::ManifestVersionUnknown`], [`BackupError::ManifestAccountMismatch`],
///   [`BackupError::ManifestTooLarge`], [`BackupError::MemberNotRegularFile`],
///   [`BackupError::ManifestBackupMismatch`], [`BackupError::ManifestDisagreesWithSidecar`]
///   and [`BackupError::DumpMissing`] for an archive that is not what was signed.
/// - [`BackupError::ScratchUnusable`], [`BackupError::FreeSpace`] and
///   [`BackupError::Bound`] when this host could not do the reading, which says
///   nothing about the archive.
pub(super) fn judge_staged_import(
    host: &dyn BackupHost,
    scratch: &ImportScratch,
    staged: &Path,
    signed: &TransferManifest,
    account: &AccountName,
    backup_id: &BackupId,
) -> Result<BackupManifest, BackupError> {
    if is_encrypted_artifact(staged)? {
        return Err(BackupError::SealedArtifactNotTransferable);
    }

    let directory = open_restore_scratch(
        &scratch.base,
        &scratch.root,
        &scratch.scratch,
        scratch.owner,
    )?;
    scratch
        .floor
        .admit(host.available_bytes(&scratch.scratch))?;

    let members = scan_members(host, staged)?;

    let inner = match read_manifest(host, staged, &directory, account.as_str()) {
        Ok(manifest) => manifest,
        // Create-side wording for a read-side fact: a manifest that does not
        // parse is a finding about the artifact.
        Err(BackupError::ManifestUnwritable) => return Err(BackupError::ManifestUnreadable),
        Err(other) => return Err(other),
    };

    if inner.backup_id != backup_id.as_str() {
        return Err(BackupError::ManifestBackupMismatch);
    }
    let inner_names: BTreeSet<&str> = inner
        .databases
        .iter()
        .map(|database| database.name.as_str())
        .collect();
    let signed_names: BTreeSet<&str> = signed.databases.iter().map(String::as_str).collect();
    if inner_names != signed_names || inner.databases.len() != signed.databases.len() {
        return Err(BackupError::ManifestDisagreesWithSidecar);
    }

    require_dumps_present(account, &inner, &members)?;

    Ok(inner)
}

#[cfg(test)]
#[path = "../tests/backup/judge_staged_import_tests.rs"]
mod tests;
