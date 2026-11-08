//! The key a backup archive is sealed with, as it arrives on the wire.

use core::fmt;

use crate::validation::secrets::archive_key_error::ArchiveKeyError;

/// How many bytes the key holds: 256 bits, the size the archive cipher takes.
pub const ARCHIVE_KEY_BYTES: usize = 32;

/// How many characters the key is written in: two lowercase hexadecimal digits
/// per byte.
const ARCHIVE_KEY_HEX_LENGTH: usize = ARCHIVE_KEY_BYTES * 2;

/// A validated 256-bit key for sealing and opening a backup archive.
///
/// The panel holds the key (encrypted at rest) and sends it with every call that
/// writes or reads an encrypted archive; this agent holds it for the length of
/// that call and keeps it nowhere (docs/superpowers/notes/2026-11-07-backup-encryption-threat-note.md).
/// It is a validated type and not a `String` so that a key which is not exactly
/// 32 bytes cannot reach the cipher at all: the cipher would reject a wrong
/// length too, but only after the archive had been opened and half-written.
///
/// [`fmt::Debug`] is written by hand and prints `«redacted»`. There is no
/// [`fmt::Display`], no `Clone` and no `serde` implementation, so the key cannot
/// reach a log line, a tracing field or a second owner without a deliberate call
/// to [`ArchiveKey::expose`]. Two keys are never compared with `==`: nothing in
/// the agent has a reason to ask whether two keys are the same, and a derived
/// comparison on a secret is not constant-time.
pub struct ArchiveKey([u8; ARCHIVE_KEY_BYTES]);

impl ArchiveKey {
    /// Parses the wire spelling: exactly 64 lowercase hexadecimal characters.
    ///
    /// Lowercase only, like every digest on this wire: one spelling per value
    /// means a key that round-trips through a log-free path cannot arrive in two
    /// shapes.
    ///
    /// # Errors
    ///
    /// - [`ArchiveKeyError::WrongLength`] when `candidate` is not 64 characters.
    /// - [`ArchiveKeyError::NotLowercaseHex`] when any character is not one of
    ///   `0-9a-f`. The error names no character and no position: a rejected key
    ///   is still a secret (rules/security.md item 8).
    pub fn parse(candidate: &str) -> Result<Self, ArchiveKeyError> {
        if candidate.len() != ARCHIVE_KEY_HEX_LENGTH {
            return Err(ArchiveKeyError::WrongLength);
        }

        let mut bytes = [0_u8; ARCHIVE_KEY_BYTES];
        let (pairs, _) = candidate.as_bytes().as_chunks::<2>();
        for (slot, [high, low]) in bytes.iter_mut().zip(pairs) {
            let high = nibble(*high).ok_or(ArchiveKeyError::NotLowercaseHex)?;
            let low = nibble(*low).ok_or(ArchiveKeyError::NotLowercaseHex)?;
            *slot = (high << 4) | low;
        }

        Ok(Self(bytes))
    }

    /// The key bytes, for the one caller that hands them to the cipher.
    ///
    /// The name is greppable on purpose: every read of the key is a line a
    /// reviewer can find with one search.
    #[must_use]
    pub fn expose(&self) -> &[u8; ARCHIVE_KEY_BYTES] {
        &self.0
    }
}

/// The value of one lowercase hexadecimal digit, or `None` for any other byte.
fn nibble(digit: u8) -> Option<u8> {
    match digit {
        b'0'..=b'9' => Some(digit - b'0'),
        b'a'..=b'f' => Some(digit - b'a' + 10),
        _ => None,
    }
}

impl fmt::Debug for ArchiveKey {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str("«redacted»")
    }
}

impl Drop for ArchiveKey {
    /// Overwrites the bytes when the key goes out of scope.
    ///
    /// Best effort and nothing stronger: the compiler may elide a write to
    /// memory that is about to be freed, and the wire message the key arrived in
    /// is a `String` this type does not own. What the overwrite buys is that the
    /// ordinary case does not leave a key lying in a freed allocation. It is NOT
    /// a defence against someone who can read this process's memory — that
    /// reader is root, and root already holds every key this agent could be
    /// handed.
    fn drop(&mut self) {
        self.0 = [0_u8; ARCHIVE_KEY_BYTES];
    }
}

#[cfg(test)]
#[path = "../../tests/validation/secrets/archive_key_tests.rs"]
mod tests;
