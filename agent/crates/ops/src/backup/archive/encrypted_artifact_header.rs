//! The fixed prefix of a sealed backup archive, and what is derived from it.

use crate::backup::backup_error::BackupError;

/// The eight bytes every sealed archive begins with.
///
/// A plain archive begins with the gzip magic (`1f 8b`), so the two can never
/// be confused by looking at the first bytes — and it is the FILE that says it
/// is sealed, not the sidecar: the sidecar is a label beside the artifact, and
/// a reader that believed it would open (or fail to open) the wrong thing
/// whenever the label was wrong.
const MAGIC: [u8; 8] = *b"MARANENC";

/// The layout of the bytes after the magic. A reader refuses any other value.
const FORMAT_VERSION: u8 = 1;

/// How long the random per-archive nonce prefix is, in bytes (128 bits).
const NONCE_PREFIX_BYTES: usize = 16;

/// How many bytes the header occupies: magic, version, nonce prefix.
pub(crate) const HEADER_BYTES: usize = MAGIC.len() + 1 + NONCE_PREFIX_BYTES;

/// How many bytes of the nonce XChaCha20-Poly1305 takes (192 bits).
const NONCE_BYTES: usize = 24;

/// How many plaintext bytes one sealed chunk holds.
///
/// One mebibyte: large enough that the 16-byte tag and 4-byte length of each
/// chunk are noise beside the data, small enough that a reader holds two
/// chunks in memory and refuses a frame that claims to be larger.
pub(crate) const CHUNK_BYTES: usize = 1024 * 1024;

/// How many bytes the authentication tag adds to each chunk.
pub(crate) const TAG_BYTES: usize = 16;

/// The bytes that follow the header on the associated data of every chunk: the
/// header itself, then one byte saying whether this is the last chunk.
const ASSOCIATED_DATA_BYTES: usize = HEADER_BYTES + 1;

/// The header of a sealed archive: the one random value its nonces are made of.
///
/// # How the nonces are made, and why they cannot repeat
///
/// Every chunk is sealed under the same long-lived key, and the one thing that
/// must never happen with an AEAD is two messages under one key and one nonce.
/// The nonce of chunk `n` is `prefix || n` (a 16-byte random prefix, then the
/// chunk number as eight big-endian bytes). Within one archive the chunk number
/// never repeats. Across archives the prefix is what differs: it is 128 random
/// bits drawn from the operating system for each archive, so two archives
/// sharing a prefix is a 2^-128 event per pair — which is the entire reason for
/// the extended-nonce cipher. With a 96-bit nonce the same scheme would leave 32
/// random bits and a birthday collision after tens of thousands of backups.
///
/// # What is authenticated besides the data
///
/// The associated data of every chunk is the whole header plus a flag byte that
/// is `1` for the last chunk and `0` for every other. The header is covered, so
/// a flipped version or prefix fails the first chunk. The flag is what makes
/// TRUNCATION detectable: an archive cut at a chunk boundary ends on a chunk
/// that was sealed with the flag at `0`, and a reader that reaches the end of
/// the file opens its last chunk with the flag at `1` and is refused. An archive
/// with chunks appended is refused the same way in the other direction. Chunk
/// order is covered by the counter inside the nonce.
///
/// Nothing here is secret: the header is the first twenty-five bytes of a file
/// that anyone holding the file can read.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct EncryptedArtifactHeader {
    /// The random per-archive part of every nonce.
    nonce_prefix: [u8; NONCE_PREFIX_BYTES],
}

impl EncryptedArtifactHeader {
    /// Draws a fresh nonce prefix from the operating system.
    ///
    /// # Errors
    ///
    /// [`BackupError::ArtifactEncryptionFailed`] when the system refuses to
    /// provide randomness. There is no fallback: a prefix from anywhere weaker
    /// would be a nonce nobody can vouch for.
    pub(crate) fn generate() -> Result<Self, BackupError> {
        let mut nonce_prefix = [0_u8; NONCE_PREFIX_BYTES];
        getrandom::fill(&mut nonce_prefix).map_err(|_| BackupError::ArtifactEncryptionFailed)?;

        Ok(Self { nonce_prefix })
    }

    /// Reads a header from the first bytes of a file.
    ///
    /// Answers `None` for a wrong magic or an unknown version; the caller turns
    /// that into the one refusal a reader has for "this is not an archive I can
    /// open".
    pub(crate) fn parse(bytes: &[u8; HEADER_BYTES]) -> Option<Self> {
        if bytes[..MAGIC.len()] != MAGIC || bytes[MAGIC.len()] != FORMAT_VERSION {
            return None;
        }

        let mut nonce_prefix = [0_u8; NONCE_PREFIX_BYTES];
        nonce_prefix.copy_from_slice(&bytes[MAGIC.len() + 1..]);

        Some(Self { nonce_prefix })
    }

    /// Whether `prefix` — the first bytes of a file — is a sealed archive's
    /// magic. Reads no further than the magic: whether the rest is a usable
    /// header is [`Self::parse`]'s question.
    pub(crate) fn has_magic(prefix: &[u8]) -> bool {
        prefix.len() >= MAGIC.len() && prefix[..MAGIC.len()] == MAGIC
    }

    /// The bytes written at the start of the file.
    pub(crate) fn to_bytes(&self) -> [u8; HEADER_BYTES] {
        let mut bytes = [0_u8; HEADER_BYTES];
        bytes[..MAGIC.len()].copy_from_slice(&MAGIC);
        bytes[MAGIC.len()] = FORMAT_VERSION;
        bytes[MAGIC.len() + 1..].copy_from_slice(&self.nonce_prefix);

        bytes
    }

    /// The nonce of chunk number `index`: the prefix, then the number.
    pub(crate) fn nonce(&self, index: u64) -> [u8; NONCE_BYTES] {
        let mut nonce = [0_u8; NONCE_BYTES];
        nonce[..NONCE_PREFIX_BYTES].copy_from_slice(&self.nonce_prefix);
        nonce[NONCE_PREFIX_BYTES..].copy_from_slice(&index.to_be_bytes());

        nonce
    }

    /// The associated data of a chunk: the header, then the last-chunk flag.
    pub(crate) fn associated_data(&self, last: bool) -> [u8; ASSOCIATED_DATA_BYTES] {
        let mut data = [0_u8; ASSOCIATED_DATA_BYTES];
        data[..HEADER_BYTES].copy_from_slice(&self.to_bytes());
        data[HEADER_BYTES] = u8::from(last);

        data
    }
}

#[cfg(test)]
#[path = "../../tests/backup/archive/encrypted_artifact_header_tests.rs"]
mod tests;
