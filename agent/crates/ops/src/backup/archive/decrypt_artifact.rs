//! Opening a sealed archive: the sealed file in, the plain archive out.

use std::fs::File;
use std::io::{Read, Write as _};
use std::os::unix::fs::OpenOptionsExt as _;
use std::path::Path;

use chacha20poly1305::aead::{Aead as _, Payload};
use chacha20poly1305::{KeyInit as _, XChaCha20Poly1305, XNonce};
use maran_agent_core::validation::secrets::archive_key::ArchiveKey;

use crate::backup::archive::encrypted_artifact_header::{
    CHUNK_BYTES, EncryptedArtifactHeader, HEADER_BYTES, TAG_BYTES,
};
use crate::backup::backup_error::BackupError;
use crate::backup::publish_artifact::ARTIFACT_MODE;

/// The largest frame a reader will accept: a full chunk and its tag. A length
/// beyond this is refused before a byte of it is read, so a damaged or hostile
/// file cannot make the reader allocate what the file's bytes claim.
const MAXIMUM_FRAME_BYTES: usize = CHUNK_BYTES + TAG_BYTES;

/// Opens the sealed archive `stored` and writes the plain archive to `plain`,
/// and returns how many bytes `plain` holds.
///
/// Every chunk is authenticated before a byte of it is written, and the whole
/// file is read to its end: there is no partial success. A wrong key, a flipped
/// bit anywhere (header included), a chunk removed, added or reordered, and an
/// archive cut short at a chunk boundary all end in the same refusal, and the
/// plain file written up to that point is the caller's to delete — the caller
/// holds the directory it lives in and removes that whole directory on every
/// path.
///
/// The target is created with `create_new` and mode `0600`. It is never larger
/// than the stored file, so asking the filesystem whether it has that much room
/// before calling this (as `open_readable_artifact` does) is the whole of the
/// space check.
///
/// # What a failure does NOT say
///
/// [`BackupError::ArtifactDecryptionFailed`] cannot tell a wrong key from a
/// damaged file: an AEAD that could would be leaking which of the two it was.
/// The panel knows which key it sent, so it reports the likelier cause itself.
///
/// # Errors
///
/// - [`BackupError::ArtifactDecryptionFailed`] for a file that does not open
///   under the key, as above, and for an unreadable `stored`.
/// - [`BackupError::ScratchUnusable`] when the plain file cannot be created or
///   written (a full disk included) — a fault of this host, not a verdict on
///   the archive.
pub(crate) fn decrypt_artifact(
    stored: &Path,
    plain: &Path,
    key: &ArchiveKey,
) -> Result<u64, BackupError> {
    let cipher = XChaCha20Poly1305::new_from_slice(key.expose())
        .map_err(|_| BackupError::ArtifactDecryptionFailed)?;

    let mut input = File::open(stored).map_err(|_| BackupError::ArtifactDecryptionFailed)?;
    let mut header_bytes = [0_u8; HEADER_BYTES];
    input
        .read_exact(&mut header_bytes)
        .map_err(|_| BackupError::ArtifactDecryptionFailed)?;
    let header = EncryptedArtifactHeader::parse(&header_bytes)
        .ok_or(BackupError::ArtifactDecryptionFailed)?;

    let mut output = File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(plain)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // A sealed file holds at least one frame, so an end of file here is a file
    // someone cut short at the header.
    let mut next_length = read_length(&mut input)?.ok_or(BackupError::ArtifactDecryptionFailed)?;
    let mut index: u64 = 0;
    let mut written: u64 = 0;
    let mut frame = vec![0_u8; MAXIMUM_FRAME_BYTES];

    loop {
        let frame_bytes = &mut frame[..next_length];
        input
            .read_exact(frame_bytes)
            .map_err(|_| BackupError::ArtifactDecryptionFailed)?;

        // Whether this frame is the last is learned by reading what follows it:
        // the end of the file is where the last flag it was sealed with is
        // checked, so a truncated archive fails here and not at the end.
        let following = read_length(&mut input)?;
        let last = following.is_none();

        let nonce = XNonce::try_from(header.nonce(index).as_slice())
            .map_err(|_| BackupError::ArtifactDecryptionFailed)?;
        let associated_data = header.associated_data(last);
        let chunk = cipher
            .decrypt(
                &nonce,
                Payload {
                    msg: frame_bytes,
                    aad: &associated_data,
                },
            )
            .map_err(|_| BackupError::ArtifactDecryptionFailed)?;

        output
            .write_all(&chunk)
            .map_err(|_| BackupError::ScratchUnusable)?;
        written += chunk.len() as u64;

        let Some(length) = following else {
            break;
        };
        next_length = length;
        index = index
            .checked_add(1)
            .ok_or(BackupError::ArtifactDecryptionFailed)?;
    }

    output
        .sync_all()
        .map_err(|_| BackupError::ScratchUnusable)?;

    Ok(written)
}

/// Reads the four-byte length that opens a frame.
///
/// `Ok(None)` is the end of the file exactly at a frame boundary, which is the
/// normal end of a sealed archive. Anything else that is not a whole, plausible
/// length is a refusal: one to three bytes then the end, a length smaller than a
/// tag (no chunk is that small), and a length beyond [`MAXIMUM_FRAME_BYTES`].
///
/// # Errors
///
/// [`BackupError::ArtifactDecryptionFailed`] as above.
fn read_length(input: &mut impl Read) -> Result<Option<usize>, BackupError> {
    let mut bytes = [0_u8; 4];
    let mut filled = 0;
    while filled < bytes.len() {
        let read = input
            .read(&mut bytes[filled..])
            .map_err(|_| BackupError::ArtifactDecryptionFailed)?;
        if read == 0 {
            break;
        }
        filled += read;
    }

    if filled == 0 {
        return Ok(None);
    }
    if filled < bytes.len() {
        return Err(BackupError::ArtifactDecryptionFailed);
    }

    let length = usize::try_from(u32::from_be_bytes(bytes))
        .map_err(|_| BackupError::ArtifactDecryptionFailed)?;
    if !(TAG_BYTES..=MAXIMUM_FRAME_BYTES).contains(&length) {
        return Err(BackupError::ArtifactDecryptionFailed);
    }

    Ok(Some(length))
}

#[cfg(test)]
#[path = "../../tests/backup/archive/decrypt_artifact_tests.rs"]
mod tests;
