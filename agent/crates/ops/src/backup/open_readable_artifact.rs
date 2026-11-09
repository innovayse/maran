//! The path a stored artifact is read through: itself, or its decrypted copy.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::secrets::archive_key::ArchiveKey;

use crate::backup::archive::decrypt_artifact::decrypt_artifact;
use crate::backup::archive::is_encrypted_artifact::is_encrypted_artifact;
use crate::backup::backup_error::BackupError;
use crate::backup::require_scratch_room::require_scratch_room;

/// The name of the decrypted copy inside the scratch.
const DECRYPTED_FILE: &str = "artifact.decrypted.tar.gz";

/// Answers the path every reader of the artifact — the member scan, the
/// manifest read, the two extractions — should open.
///
/// A plain artifact is read where it lies: the answer is `stored` itself, and
/// `key` is not looked at. That is the whole of "existing backups stay
/// readable", and also why a key sent for an artifact that was never sealed is
/// harmless.
///
/// A sealed one is decrypted ONCE into `scratch` and the copy's path is the
/// answer. The copy is plain bytes of the customer's whole home and every
/// database on disk, in the root-only scratch the caller already owns, for as
/// long as the operation runs; the caller removes the scratch on every path out,
/// the start-up reaper removes anything an interrupted run left in it, and the
/// copy is `0600` and root's from its first byte. This is the residue sealing
/// at rest cannot avoid on the READING side, and it is stated in the threat note
/// rather than hidden.
///
/// The digest the caller checked against the panel's record is over the STORED
/// bytes (the ciphertext), so decrypting after that check is reading what the
/// panel took. It is reopened by path, exactly as the readers that follow
/// reopen the plain artifact by path, inside a directory only root can write.
///
/// # Errors
///
/// - [`BackupError::EncryptionKeyRequired`] for a sealed artifact and no key.
/// - [`BackupError::ArtifactDecryptionFailed`] for a sealed artifact that does
///   not open under the key.
/// - [`BackupError::ScratchTooSmall`] / [`BackupError::ScratchUnmeasurable`]
///   when the scratch cannot take a copy as large as the stored file — asked
///   before the first byte is written.
/// - [`BackupError::ScratchUnusable`] when the copy cannot be written.
/// - [`BackupError::ChecksumUnreadable`] when the artifact cannot be opened to
///   look at its first bytes.
pub(crate) fn open_readable_artifact(
    stored: &Path,
    scratch: &Path,
    key: Option<&ArchiveKey>,
) -> Result<PathBuf, BackupError> {
    if !is_encrypted_artifact(stored)? {
        return Ok(stored.to_path_buf());
    }

    let key = key.ok_or(BackupError::EncryptionKeyRequired)?;

    let stored_bytes = stored
        .symlink_metadata()
        .map_err(|_| BackupError::ChecksumUnreadable)?
        .len();
    require_scratch_room(scratch, stored_bytes)?;

    let plain = scratch.join(DECRYPTED_FILE);
    decrypt_artifact(stored, &plain, key)?;

    Ok(plain)
}

#[cfg(test)]
#[path = "../tests/backup/open_readable_artifact_tests.rs"]
mod tests;
