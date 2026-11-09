//! ExportBackup: reading one published artifact out of this host together with a
//! manifest signed for the host that will receive it.

use std::fs::File;
use std::io::{ErrorKind, Read as _};
use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;
use sha2::{Digest as _, Sha256};

use crate::accounts::take_account_lock;
use crate::backup::archive::is_encrypted_artifact::is_encrypted_artifact;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_root::open_account_directory;
use crate::backup::model::export_sink::ExportSink;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::package::lowercase_hex::lowercase_hex;
use crate::backup::package::package_signing_key::PackageSigningKey;
use crate::backup::package::signed_manifest::SignedManifest;
use crate::backup::package::transfer_manifest::{TRANSFER_MANIFEST_VERSION, TransferManifest};
use crate::backup::package::verify_package::verify_package;
use crate::backup::read_sidecar::read_sidecar;
use crate::backup::verify_backup::{is_plain_file_owned_by, is_sha256_hex};

/// The uid the artifact, its sidecar and the account's backup directory must
/// belong to on a real host.
const ROOT_UID: u32 = 0;

/// The most bytes one chunk carries, and the most this operation holds in memory
/// at once: one buffer of this size, whatever the artifact weighs.
const CHUNK_BYTES: usize = 1024 * 1024;

/// Reads the artifact `backup_id` names for `account` out through `sink`, after a
/// manifest signed with `key`, and answers how many artifact bytes were sent.
///
/// # What is sent, and what is checked on the way
///
/// The artifact is sent byte for byte as it lies on disk. The manifest it is
/// preceded by claims the size and SHA-256 the account's sidecar recorded when the
/// backup was made, plus the account, the backup id, the database names and the
/// fingerprint of `key`. Before anything is sent the artifact is checked to be a
/// plain, root-owned, singly linked file of exactly the recorded size and not a
/// sealed archive; while it is sent it is hashed, and when the last byte has gone
/// the digest is compared with the recorded one. A mismatch ends the export with an
/// error, so the receiver — which checks the digest it was signed — and the sender
/// refuse the same artifact. The manifest is verified against `key`'s own
/// fingerprint before it is sent, so a key that does not sign correctly fails here
/// and not at the far end.
///
/// # The lock
///
/// The account's backup lock is taken first and never waited for, and it is held
/// until the last byte has been handed to the sink: a restore replacing the home or
/// a delete removing the artifact must not run under a reader. A scheduled backup
/// of the same account is refused for that time ([`BackupError::AlreadyRunning`]),
/// which an operator may find surprising for a multi-gigabyte export. The lock is
/// process-local, like every lock in this agent.
///
/// # Cancellation class
///
/// **Stop.** The only product of the work is the stream, and it writes nothing on
/// the host, so a sink that reports the consumer gone ends the read with
/// [`BackupError::ExportStopped`] and releases the lock. Nothing is left to
/// reclaim.
///
/// # Errors
///
/// - [`BackupError::AlreadyRunning`] when the account's lock is held.
/// - [`BackupError::NotFound`] when the account has no backup directory or the
///   directory holds no artifact of that id.
/// - [`BackupError::SidecarUnreadable`], [`BackupError::ArtifactNotRegularFile`],
///   [`BackupError::ArtifactSizeMismatch`], [`BackupError::ChecksumMismatch`],
///   [`BackupError::ManifestAccountMismatch`] and [`BackupError::ManifestBackupMismatch`]
///   for an artifact that is not what its record says.
/// - [`BackupError::SealedArtifactNotTransferable`] for a sealed archive.
/// - [`BackupError::PackageSigningKeyRejected`] when `key` cannot sign.
/// - [`BackupError::ExportStopped`] when the sink stopped taking bytes.
/// - [`BackupError::ChecksumUnreadable`] when the artifact cannot be read.
/// - [`BackupError::BackupRootUnsafe`] and [`BackupError::BackupRootUnusable`]
///   from the inode checks of the backup root.
pub fn export_backup(
    account: &AccountName,
    backup_id: &BackupId,
    root: &LocalBackupRoot,
    key: &PackageSigningKey,
    sink: &mut dyn ExportSink,
) -> Result<u64, BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let outcome = match open_account_directory(root, account) {
        Ok(Some(directory)) => export_in(ROOT_UID, &directory, account, backup_id, key, sink),
        Ok(None) => Err(BackupError::NotFound),
        Err(error) => Err(error),
    };

    drop(guard);
    outcome
}

/// The body of [`export_backup`], with the account's directory and the owner every
/// file must belong to injected.
///
/// Split for the reason the verification's body is: a test runs as an ordinary
/// user and cannot create a root-owned file, so a body that hard-coded root could
/// be exercised only on its refusing path.
///
/// # Errors
///
/// As documented on [`export_backup`], except for the lock and the directory.
fn export_in(
    owner: u32,
    directory: &Path,
    account: &AccountName,
    backup_id: &BackupId,
    key: &PackageSigningKey,
    sink: &mut dyn ExportSink,
) -> Result<u64, BackupError> {
    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    if !is_plain_file_owned_by(&artifact, owner)? {
        return Err(BackupError::NotFound);
    }
    if !is_plain_file_owned_by(&sidecar, owner)? {
        return Err(BackupError::SidecarUnreadable);
    }

    // The sidecar is a label and the artifact is the thing: what it claims is
    // checked below against the bytes, and again as they are sent.
    let recorded = read_sidecar(&sidecar).map_err(|_reason| BackupError::SidecarUnreadable)?;
    if !is_sha256_hex(&recorded.artifact_sha256) {
        return Err(BackupError::SidecarUnreadable);
    }
    if recorded.manifest.account != account.as_str() {
        return Err(BackupError::ManifestAccountMismatch);
    }
    if recorded.manifest.backup_id != backup_id.as_str() {
        return Err(BackupError::ManifestBackupMismatch);
    }
    if recorded.encrypted || is_encrypted_artifact(&artifact)? {
        return Err(BackupError::SealedArtifactNotTransferable);
    }

    let mut file = File::open(&artifact).map_err(|_| BackupError::ChecksumUnreadable)?;
    let metadata = file
        .metadata()
        .map_err(|_| BackupError::ChecksumUnreadable)?;
    if !metadata.is_file() || metadata.nlink() != 1 || metadata.uid() != owner {
        return Err(BackupError::ArtifactNotRegularFile);
    }
    if metadata.len() != recorded.artifact_bytes {
        return Err(BackupError::ArtifactSizeMismatch);
    }

    let manifest = TransferManifest {
        version: TRANSFER_MANIFEST_VERSION,
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        artifact_bytes: recorded.artifact_bytes,
        artifact_sha256: recorded.artifact_sha256.clone(),
        home_bytes: recorded.manifest.home_bytes,
        databases: recorded
            .manifest
            .databases
            .iter()
            .map(|database| database.name.clone())
            .collect(),
        created_at_unix: recorded.manifest.created_at_unix,
        agent_version: recorded.manifest.agent_version.clone(),
        signer_fingerprint: key.fingerprint(),
    };
    let bytes = serde_json::to_vec(&manifest).map_err(|_| BackupError::PackageManifestMalformed)?;
    let signature = key.sign(&bytes)?;
    let signed = SignedManifest {
        manifest: bytes,
        signature,
        public_key: key.public_key(),
    };
    // What the far end will do first, done here first.
    verify_package(&signed, &key.fingerprint())
        .map_err(|_| BackupError::PackageSigningKeyRejected)?;

    sink.header(&signed)
        .map_err(|_stopped| BackupError::ExportStopped)?;

    send_artifact(
        &mut file,
        &recorded.artifact_sha256,
        recorded.artifact_bytes,
        sink,
    )
}

/// Sends `file` through `sink` in chunks, hashing it, and requires it to be
/// exactly `bytes` long and to hash to `sha256`.
///
/// Reads at most one byte past `bytes`, so an artifact that grew since it was
/// measured is refused without reading the rest of it.
///
/// # Errors
///
/// [`BackupError::ChecksumUnreadable`] for a read that failed,
/// [`BackupError::ArtifactSizeMismatch`] for a length other than `bytes`,
/// [`BackupError::ChecksumMismatch`] for another digest, and
/// [`BackupError::ExportStopped`] when the sink stopped.
fn send_artifact(
    file: &mut File,
    sha256: &str,
    bytes: u64,
    sink: &mut dyn ExportSink,
) -> Result<u64, BackupError> {
    let mut reader = file.take(bytes.saturating_add(1));
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; CHUNK_BYTES];
    let mut total: u64 = 0;

    loop {
        let read = match reader.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => read,
            Err(error) if error.kind() == ErrorKind::Interrupted => continue,
            Err(_) => return Err(BackupError::ChecksumUnreadable),
        };
        total = total.saturating_add(read as u64);
        if total > bytes {
            return Err(BackupError::ArtifactSizeMismatch);
        }
        hasher.update(&buffer[..read]);
        sink.chunk(&buffer[..read])
            .map_err(|_stopped| BackupError::ExportStopped)?;
    }

    if total != bytes {
        return Err(BackupError::ArtifactSizeMismatch);
    }
    if lowercase_hex(&hasher.finalize()) != sha256 {
        return Err(BackupError::ChecksumMismatch);
    }

    Ok(total)
}

#[cfg(test)]
#[path = "../tests/backup/export_backup_tests.rs"]
mod tests;
