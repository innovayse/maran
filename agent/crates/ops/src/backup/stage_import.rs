//! Receiving an import's bytes onto the disk, bounded and hashed.

use std::fs::{File, OpenOptions, remove_file};
use std::io::Write as _;
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;

use sha2::{Digest as _, Sha256};

use crate::backup::backup_error::BackupError;
use crate::backup::model::import_source::ImportSource;
use crate::backup::package::lowercase_hex::lowercase_hex;
use crate::backup::package::transfer_manifest::TransferManifest;
use crate::backup::publish_artifact::ARTIFACT_MODE;

/// Writes everything `source` delivers to a new file at `partial`, and requires it
/// to be exactly the artifact `manifest` declares.
///
/// The file is created with `create_new` and the artifact's mode (`0600`, root's):
/// the name is minted by the agent from a validated backup id, anything already
/// wearing it is this operation's own litter from a run that died, and it is
/// removed first. The size is enforced chunk by chunk, so a sender cannot make
/// this host write more than the signed manifest declares however long it
/// streams: the first chunk that would pass the declared length ends the stage
/// without being written. The SHA-256 is taken while writing and compared at the
/// end; the file is synced before it is judged any further.
///
/// On any error the partial file is removed by this function.
///
/// # Errors
///
/// - [`BackupError::ArtifactUnpublishable`] when the file cannot be created or
///   written (a full disk lands here too, after the floor was asked up front).
/// - [`BackupError::ImportLengthMismatch`] for more bytes than declared, or fewer.
/// - [`BackupError::ImportDigestMismatch`] for another SHA-256.
/// - [`BackupError::ImportInterrupted`] when the source ended abnormally.
pub(super) fn stage_import(
    partial: &Path,
    source: &mut dyn ImportSource,
    manifest: &TransferManifest,
) -> Result<(), BackupError> {
    let _ = remove_file(partial);

    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(partial)
        .map_err(|_| BackupError::ArtifactUnpublishable)?;

    let received = receive(&mut file, source, manifest);
    drop(file);

    if received.is_err() {
        let _ = remove_file(partial);
    }

    received
}

/// The loop of [`stage_import`], with the file already open.
///
/// # Errors
///
/// As documented on [`stage_import`].
fn receive(
    file: &mut File,
    source: &mut dyn ImportSource,
    manifest: &TransferManifest,
) -> Result<(), BackupError> {
    let mut hasher = Sha256::new();
    let mut total: u64 = 0;

    while let Some(chunk) = source
        .next_chunk()
        .map_err(|_interrupted| BackupError::ImportInterrupted)?
    {
        total = total
            .checked_add(chunk.len() as u64)
            .ok_or(BackupError::ImportLengthMismatch)?;
        if total > manifest.artifact_bytes {
            return Err(BackupError::ImportLengthMismatch);
        }
        hasher.update(chunk);
        file.write_all(chunk)
            .map_err(|_| BackupError::ArtifactUnpublishable)?;
    }

    if total != manifest.artifact_bytes {
        return Err(BackupError::ImportLengthMismatch);
    }
    if lowercase_hex(&hasher.finalize()) != manifest.artifact_sha256 {
        return Err(BackupError::ImportDigestMismatch);
    }

    file.sync_all()
        .map_err(|_| BackupError::ArtifactUnpublishable)
}

#[cfg(test)]
#[path = "../tests/backup/stage_import_tests.rs"]
mod tests;
