//! Whether a stored artifact is sealed, decided by its own first bytes.

use std::fs::File;
use std::io::Read as _;
use std::path::Path;

use crate::backup::archive::encrypted_artifact_header::EncryptedArtifactHeader;
use crate::backup::backup_error::BackupError;

/// How many bytes are read to decide: the length of the magic.
const SNIFF_BYTES: usize = 8;

/// Whether the file at `path` begins with a sealed archive's magic.
///
/// The FILE decides, never the sidecar. A reader that took the sidecar's word
/// would treat a downgraded or mislabelled artifact as whatever its label said,
/// and the label is the one part of a backup that is written without any
/// secret. A file shorter than the magic is not sealed.
///
/// # Errors
///
/// [`BackupError::ChecksumUnreadable`] when the file cannot be opened or read —
/// the same refusal the digest gives for the same file, because a reader that
/// cannot open the artifact has no other thing to say about it.
pub(crate) fn is_encrypted_artifact(path: &Path) -> Result<bool, BackupError> {
    let mut file = File::open(path).map_err(|_| BackupError::ChecksumUnreadable)?;
    let mut prefix = [0_u8; SNIFF_BYTES];
    let mut filled = 0;
    while filled < SNIFF_BYTES {
        let read = file
            .read(&mut prefix[filled..])
            .map_err(|_| BackupError::ChecksumUnreadable)?;
        if read == 0 {
            break;
        }
        filled += read;
    }

    Ok(EncryptedArtifactHeader::has_magic(&prefix[..filled]))
}

#[cfg(test)]
#[path = "../../tests/backup/archive/is_encrypted_artifact_tests.rs"]
mod tests;
