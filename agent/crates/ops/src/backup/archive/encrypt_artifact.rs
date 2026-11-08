//! Sealing a finished archive: the plain file in, a sealed file out.

use std::fs::File;
use std::io::{Read, Write as _};
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;

use chacha20poly1305::aead::{Aead as _, Payload};
use chacha20poly1305::{KeyInit as _, XChaCha20Poly1305, XNonce};
use maran_agent_core::validation::secrets::archive_key::ArchiveKey;

use crate::backup::archive::encrypted_artifact_header::{CHUNK_BYTES, EncryptedArtifactHeader};
use crate::backup::backup_error::BackupError;
use crate::backup::publish_artifact::ARTIFACT_MODE;

/// Reads `plain` and writes it to `sealed` as a sealed archive, and returns how
/// many bytes `sealed` holds.
///
/// # Layout of what is written
///
/// The header ([`EncryptedArtifactHeader`]), then one frame per chunk of up to
/// 1 MiB of the plain file: a four-byte big-endian length, then that many bytes
/// of ciphertext with its 16-byte tag. The last frame is sealed with the
/// last-chunk flag set, so an empty input still produces one frame (a tag over
/// nothing) and the file always ends on a frame that says it is the end.
///
/// The target is created with `create_new` and mode `0600`, like every other
/// file this area publishes: it cannot follow a link somebody planted at the
/// name and it is root's alone from its first byte. It is flushed to disk before
/// this returns, because the next step renames it over the only other copy.
///
/// Memory is two chunks and one sealed chunk, whatever the archive's size.
///
/// # Errors
///
/// [`BackupError::ArtifactEncryptionFailed`] for a key the cipher rejects, a
/// file that cannot be read or written (a full disk included), or randomness the
/// system would not give. The partial `sealed` file is left for the caller to
/// remove: it knows the name, and a removal written here and again there is a
/// removal missing from one of them.
pub(crate) fn encrypt_artifact(
    plain: &Path,
    sealed: &Path,
    key: &ArchiveKey,
) -> Result<u64, BackupError> {
    let cipher = XChaCha20Poly1305::new_from_slice(key.expose())
        .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
    let header = EncryptedArtifactHeader::generate()?;

    let mut input = File::open(plain).map_err(|_| BackupError::ArtifactEncryptionFailed)?;
    let mut output = File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(sealed)
        .map_err(|_| BackupError::ArtifactEncryptionFailed)?;

    output
        .write_all(&header.to_bytes())
        .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
    let mut written = header.to_bytes().len() as u64;

    // One chunk of lookahead: a chunk is the last one exactly when nothing
    // follows it, and the only way to know that before sealing it is to have
    // read what follows.
    let mut current = vec![0_u8; CHUNK_BYTES];
    let mut following = vec![0_u8; CHUNK_BYTES];
    let mut current_length = fill(&mut input, &mut current)?;
    let mut index: u64 = 0;

    loop {
        let following_length = fill(&mut input, &mut following)?;
        let last = following_length == 0;

        let nonce = XNonce::try_from(header.nonce(index).as_slice())
            .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
        let associated_data = header.associated_data(last);
        let frame = cipher
            .encrypt(
                &nonce,
                Payload {
                    msg: &current[..current_length],
                    aad: &associated_data,
                },
            )
            .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
        let length =
            u32::try_from(frame.len()).map_err(|_| BackupError::ArtifactEncryptionFailed)?;

        output
            .write_all(&length.to_be_bytes())
            .and_then(|()| output.write_all(&frame))
            .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
        written += u64::from(length) + 4;

        if last {
            break;
        }
        std::mem::swap(&mut current, &mut following);
        current_length = following_length;
        index = index
            .checked_add(1)
            .ok_or(BackupError::ArtifactEncryptionFailed)?;
    }

    output
        .sync_all()
        .map_err(|_| BackupError::ArtifactEncryptionFailed)?;

    Ok(written)
}

/// Reads from `input` until `buffer` is full or the file ends, and returns how
/// many bytes it holds. A short count means the end of the file, never a
/// partial read: `Read::read` may return fewer bytes than are left.
///
/// # Errors
///
/// [`BackupError::ArtifactEncryptionFailed`] when a read fails.
fn fill(input: &mut impl Read, buffer: &mut [u8]) -> Result<usize, BackupError> {
    let mut filled = 0;
    while filled < buffer.len() {
        let read = input
            .read(&mut buffer[filled..])
            .map_err(|_| BackupError::ArtifactEncryptionFailed)?;
        if read == 0 {
            break;
        }
        filled += read;
    }

    Ok(filled)
}

#[cfg(test)]
#[path = "../../tests/backup/archive/encrypt_artifact_tests.rs"]
mod tests;
