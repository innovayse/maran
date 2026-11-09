//! ImportBackup: receiving one backup artifact exported by another host and
//! publishing it as a backup of this one.

use std::fs::{remove_dir_all, remove_file};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_root::{
    open_account_directory_owned_by, prepare_account_directory_owned_by,
};
use crate::backup::create_backup::MAXIMUM_ARCHIVE_BYTES;
use crate::backup::import_scratch::ImportScratch;
use crate::backup::judge_staged_import::judge_staged_import;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::import_request::ImportRequest;
use crate::backup::model::import_source::ImportSource;
use crate::backup::model::imported_backup::ImportedBackup;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::package::transfer_manifest::TransferManifest;
use crate::backup::package::verify_package::verify_package;
use crate::backup::publish_artifact::publish_artifact;
use crate::backup::read_sidecar::read_sidecar;
use crate::backup::stage_import::stage_import;
use crate::backup::write_sidecar::write_sidecar;

/// The uid every file and directory an import creates or reads in must belong to
/// on a real host.
const ROOT_UID: u32 = 0;

/// The suffix of the file an import is staged into.
const PARTIAL_SUFFIX: &str = ".partial";

/// Verifies the package `request` carries, receives its artifact from `source`
/// and publishes it under the source's backup id, marked as imported.
///
/// # Order, and why nothing is touched before it has held
///
/// 1. The fingerprint the operator confirmed equals the key's, and the signature
///    verifies over the exact manifest bytes ([`verify_package`]).
/// 2. The manifest's fields are valid ([`TransferManifest::validated`]) and its
///    declared size is within the ceiling: the smaller of the request's and
///    `MAXIMUM_ARCHIVE_BYTES`.
/// 3. The account's backup lock is taken, never waited for.
/// 4. A backup of this id that is already published settles the call: the same
///    digest is a converged retry (`already_present`), another is
///    [`BackupError::ImportBackupIdTaken`]. The digest is the one of the artifact
///    on disk, hashed again for the question, never the sidecar's record of it.
/// 5. No system user of the account's name exists (decision D1: an imported
///    account keeps its name, so a name already taken is a conflict). Any entry
///    of that name counts, including one the identity rules refuse, and a
///    password database that cannot be read refuses the import rather than
///    being read as "nobody has this name" ([`BackupHost::account_exists`]).
/// 6. The filesystem keeps the free-space floor after the declared size is
///    written.
///
/// Only then is the account's backup directory created, the artifact staged, hashed
/// while written and required to be exactly the declared length and digest, judged
/// by the member scan and manifest comparison of
/// `judge_staged_import`, and published by a rename. The sidecar is written last,
/// carrying `imported: true`, which is what routes a later restore of the artifact's
/// dumps through the restricted loader.
///
/// # What it does not do
///
/// It creates no account, restores nothing and loads no SQL. The panel creates the
/// account and then asks for an ordinary restore; this call only makes the artifact
/// a backup of this host.
///
/// # Cancellation class
///
/// **Stop and reclaim.** A source that ends abnormally aborts the import: the staged
/// file and the scratch are removed on every path and nothing is published, because
/// publishing is the final rename and there is no midpoint to protect. The only thing
/// that can remain is an empty account backup directory.
///
/// # Errors
///
/// - [`BackupError::PackageFingerprintMismatch`], [`BackupError::PackageSignatureInvalid`]
///   and [`BackupError::PackageManifestMalformed`] from the verification.
/// - [`BackupError::ImportTooLarge`] for a declared size over the ceiling.
/// - [`BackupError::AlreadyRunning`] when the account's lock is held.
/// - [`BackupError::ImportBackupIdTaken`] and [`BackupError::ImportAccountExists`].
/// - [`BackupError::ChecksumUnreadable`] when a published artifact of this id cannot
///   be read to the end to be compared.
/// - [`BackupError::ExtractionIdentityUnavailable`] when the password database
///   cannot be read.
/// - [`BackupError::FreeSpace`] when the filesystem lacks the room.
/// - [`BackupError::ImportLengthMismatch`], [`BackupError::ImportDigestMismatch`]
///   and [`BackupError::ImportInterrupted`] from the stream.
/// - The findings of `judge_staged_import`, and
///   [`BackupError::ArtifactUnpublishable`] when the staged file cannot be written or
///   published.
/// - [`BackupError::BackupRootUnsafe`] and [`BackupError::BackupRootUnusable`] from
///   the inode checks of the backup root.
pub fn import_backup(
    host: &dyn BackupHost,
    request: &ImportRequest,
    source: &mut dyn ImportSource,
    root: &LocalBackupRoot,
    floor: &FreeSpaceFloor,
) -> Result<ImportedBackup, BackupError> {
    let manifest = verify_package(&request.signed, &request.expected_fingerprint)?;
    let (account, backup_id) = manifest.validated()?;

    let limit = match request.max_bytes {
        0 => MAXIMUM_ARCHIVE_BYTES,
        asked => asked.min(MAXIMUM_ARCHIVE_BYTES),
    };
    if manifest.artifact_bytes > limit {
        return Err(BackupError::ImportTooLarge {
            declared: manifest.artifact_bytes,
            limit,
        });
    }

    let guard = take_account_lock(&account).ok_or(BackupError::AlreadyRunning)?;

    let resolved = root
        .resolve()
        .map_err(|_| BackupError::BackupRootUnsafe { uid: 0, mode: 0 });
    let outcome = resolved.and_then(|backup_root| {
        let scratch = ImportScratch {
            base: PathBuf::from("/"),
            root: PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
            scratch: AgentPaths::backup_import_scratch_dir(&backup_id),
            owner: ROOT_UID,
            floor: *floor,
        };

        import_in(
            host,
            &scratch,
            &backup_root,
            source,
            &manifest,
            &account,
            &backup_id,
        )
    });

    drop(guard);
    outcome
}

/// The body of [`import_backup`], with the resolved backup root and the scratch
/// injected (the owner every level must belong to rides in `scratch`).
///
/// Split for the reason the verification's body is: a test cannot create a
/// root-owned directory, so a body that read its own placement could be exercised
/// only on its refusing path.
///
/// # Errors
///
/// As documented on [`import_backup`], from step 4 on.
fn import_in(
    host: &dyn BackupHost,
    scratch: &ImportScratch,
    backup_root: &Path,
    source: &mut dyn ImportSource,
    manifest: &TransferManifest,
    account: &AccountName,
    backup_id: &BackupId,
) -> Result<ImportedBackup, BackupError> {
    if let Some(existing) = open_account_directory_owned_by(backup_root, account, scratch.owner)?
        && existing
            .join(artifact_file_name(backup_id))
            .symlink_metadata()
            .is_ok()
    {
        return already_published(&existing, account, backup_id, manifest);
    }

    if host.account_exists(account)? {
        return Err(BackupError::ImportAccountExists);
    }

    let directory = prepare_account_directory_owned_by(backup_root, account, scratch.owner)?;

    // The room that would be left once the artifact is written, so that a
    // package that fits but would starve the host is refused before a byte lands.
    scratch.floor.admit(
        host.available_bytes(&directory)
            .map(|available| available.saturating_sub(manifest.artifact_bytes)),
    )?;

    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));
    let mut partial = artifact.clone().into_os_string();
    partial.push(PARTIAL_SUFFIX);
    let partial = PathBuf::from(partial);

    let judged = stage_import(&partial, source, manifest)
        .and_then(|()| judge_staged_import(host, scratch, &partial, manifest, account, backup_id));
    // Always, on every path: the scratch holds a copy of the sender's manifest
    // and a scratch that outlives its operation is litter nobody is watching.
    let _ = remove_dir_all(&scratch.scratch);
    let inner = match judged {
        Ok(inner) => inner,
        Err(error) => {
            let _ = remove_file(&partial);
            return Err(error);
        }
    };

    if let Err(error) = publish_artifact(&partial, &artifact, scratch.owner) {
        let _ = remove_file(&partial);
        return Err(error);
    }

    let database_count = u32::try_from(inner.databases.len()).unwrap_or(u32::MAX);
    let summary = BackupSummary::readable(
        backup_id.as_str().to_owned(),
        inner,
        manifest.artifact_bytes,
        manifest.artifact_sha256.clone(),
    )
    .imported();
    write_sidecar(&sidecar, &artifact, &summary, scratch.owner)?;

    Ok(ImportedBackup {
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        artifact_bytes: manifest.artifact_bytes,
        artifact_sha256: manifest.artifact_sha256.clone(),
        database_count,
        already_present: false,
    })
}

/// Answers an import of an artifact that is already published: success for the
/// same digest and size, a refusal for anything else.
///
/// The sidecar is a record written beside the artifact, not the artifact: a sidecar
/// that agrees with the package says nothing about the bytes on disk (a sidecar
/// copied from another host, an artifact replaced or damaged since it was
/// published). Answering `already_present` for such a pair would tell the panel its
/// package is on this host when what is there is something else, so the artifact
/// itself is hashed again and must match the package's digest and size too. The
/// read is bounded the way every other hashing of an artifact is (streamed in
/// fixed chunks, under the account's lock the caller holds).
///
/// # Errors
///
/// - [`BackupError::ImportBackupIdTaken`] when the sidecar is missing or unreadable,
///   records another digest or size, or the artifact on disk does not hash to the
///   package's digest and size.
/// - [`BackupError::ChecksumUnreadable`] when the artifact cannot be read to the end:
///   a fault of this host, which is not a statement about what the id holds.
fn already_published(
    directory: &Path,
    account: &AccountName,
    backup_id: &BackupId,
    manifest: &TransferManifest,
) -> Result<ImportedBackup, BackupError> {
    let recorded = read_sidecar(&directory.join(sidecar_file_name(backup_id)))
        .map_err(|_reason| BackupError::ImportBackupIdTaken)?;

    if recorded.artifact_sha256 != manifest.artifact_sha256
        || recorded.artifact_bytes != manifest.artifact_bytes
    {
        return Err(BackupError::ImportBackupIdTaken);
    }

    let (actual_sha256, actual_bytes) =
        checksum_file(&directory.join(artifact_file_name(backup_id)))?;
    if actual_sha256 != manifest.artifact_sha256 || actual_bytes != manifest.artifact_bytes {
        return Err(BackupError::ImportBackupIdTaken);
    }

    Ok(ImportedBackup {
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        artifact_bytes: recorded.artifact_bytes,
        artifact_sha256: recorded.artifact_sha256,
        database_count: u32::try_from(recorded.manifest.databases.len()).unwrap_or(u32::MAX),
        already_present: true,
    })
}

#[cfg(test)]
#[path = "../tests/backup/import_backup_tests.rs"]
mod tests;
