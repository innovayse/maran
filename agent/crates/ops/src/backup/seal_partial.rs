//! Sealing the finished `.partial` archive in place, before it is hashed.

use std::fs::{remove_file, rename};
use std::path::{Path, PathBuf};

use maran_agent_core::validation::secrets::archive_key::ArchiveKey;

use crate::backup::archive::encrypt_artifact::encrypt_artifact;
use crate::backup::backup_error::BackupError;

/// The suffix the sealed copy wears while it is being written, beside the plain
/// `.partial` it is made from.
const SEALING_SUFFIX: &str = ".sealing";

/// Replaces the plain archive at `partial` with its sealed form, under the same
/// name, and answers how many bytes the sealed file holds.
///
/// The sealed copy is written beside the plain one under a name of its own and
/// then renamed over it. The rename is the point: at no instant is there a file
/// named `partial` that is half plain and half sealed, and the plain inode is
/// unlinked by the same call that publishes the sealed one, so a crash leaves
/// either the plain archive (not yet published, so never advertised) or the
/// sealed one. The caller hashes `partial` afterwards, so the digest the panel
/// records is over the bytes that will be read back.
///
/// **What this does not do**: it does not wipe the plain archive's blocks.
/// Between the archiver finishing and this rename the plain archive existed on
/// the backup volume, root-only (`0600` in a `0700` directory); after it, the
/// blocks are free but not overwritten, on a filesystem that may not overwrite
/// in place anyway. Sealing protects the artifact as published, copied or
/// archived; a volume that must also protect its own free blocks needs
/// full-disk encryption beneath it. The threat note carries that limit.
///
/// # Errors
///
/// [`BackupError::ArtifactEncryptionFailed`]. On any failure the half-written
/// sealed file is removed here; the plain `partial` is the caller's to remove,
/// as it is for every other failure of the creation.
pub(crate) fn seal_partial(partial: &Path, key: &ArchiveKey) -> Result<u64, BackupError> {
    let mut sealing = partial.as_os_str().to_owned();
    sealing.push(SEALING_SUFFIX);
    let sealing = PathBuf::from(sealing);

    // A leftover from a run that died here is this operation's own litter; the
    // name is derived from a backup id nothing else mints.
    let _ = remove_file(&sealing);

    let outcome = encrypt_artifact(partial, &sealing, key).and_then(|bytes| {
        rename(&sealing, partial).map_err(|_| BackupError::ArtifactEncryptionFailed)?;
        Ok(bytes)
    });
    if outcome.is_err() {
        let _ = remove_file(&sealing);
    }

    outcome
}

#[cfg(test)]
#[path = "../tests/backup/seal_partial_tests.rs"]
mod tests;
