//! The private key the panel presents to a migration source.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use crate::secret_string::SecretString;

use super::source_private_key_error::SourcePrivateKeyError;

/// The line a key document starts with.
const HEADER: &str = "-----BEGIN OPENSSH PRIVATE KEY-----";

/// The line a key document ends with.
const FOOTER: &str = "-----END OPENSSH PRIVATE KEY-----";

/// The bytes a decoded OpenSSH private key document starts with (PROTOCOL.key).
const MAGIC: &[u8] = b"openssh-key-v1\0";

/// The longest base64 line accepted. `ssh-keygen` writes 70-character lines; the
/// ceiling only bounds a hostile line, it does not enforce the wrapping.
const MAX_LINE_LENGTH: usize = 256;

/// An OpenSSH private key held for the length of one call.
///
/// The panel keeps the private half of a source's key encrypted at rest and sends
/// it with every call that connects; the agent keeps it only in the scratch
/// directory it removes on every path (docs/superpowers/notes/2026-11-07-server-migration-threat-note.md).
/// This type is the proof that the text is a key document and nothing else
/// before it is written to a file that `ssh` reads: printable ASCII and line
/// feeds only, the OpenSSH header and footer as the first and last line, and a
/// body that is one base64 document starting with the `openssh-key-v1` magic. It
/// is bounded at 16 KiB.
///
/// The text is held behind a [`SecretString`], so `Debug` and `Display` print
/// `«redacted»`, and the only reader is [`SourcePrivateKey::expose`]. There is no
/// `serde` implementation and no comparison: nothing in the agent has a reason to
/// ask whether two private keys are the same.
///
/// The text is kept with exactly one trailing line feed whatever the wire had,
/// because `ssh` refuses a key file that does not end with one.
#[derive(Debug, Clone)]
pub struct SourcePrivateKey(SecretString);

impl SourcePrivateKey {
    /// The longest key document accepted, in bytes. An ed25519 key is under
    /// 500 bytes and a 16384-bit RSA key under 13 KiB.
    pub const MAX_LENGTH: usize = 16 * 1024;

    /// Parses the wire bytes as an OpenSSH private key document.
    ///
    /// # Errors
    ///
    /// - [`SourcePrivateKeyError::Empty`] when `candidate` is empty.
    /// - [`SourcePrivateKeyError::TooLong`] beyond [`Self::MAX_LENGTH`] bytes.
    /// - [`SourcePrivateKeyError::ForbiddenCharacter`] for any byte other than
    ///   printable ASCII and a line feed, checked before the text is split.
    /// - [`SourcePrivateKeyError::MissingHeader`] unless the first line is the
    ///   OpenSSH header.
    /// - [`SourcePrivateKeyError::MissingFooter`] unless the last line is the
    ///   OpenSSH footer, with nothing after it but one optional line feed.
    /// - [`SourcePrivateKeyError::MalformedBody`] unless the lines between are
    ///   non-empty base64 lines that decode, joined, to a document starting with
    ///   the `openssh-key-v1` magic.
    pub fn parse(candidate: &[u8]) -> Result<Self, SourcePrivateKeyError> {
        if candidate.is_empty() {
            return Err(SourcePrivateKeyError::Empty);
        }
        if candidate.len() > Self::MAX_LENGTH {
            return Err(SourcePrivateKeyError::TooLong {
                limit: Self::MAX_LENGTH,
            });
        }
        if !candidate
            .iter()
            .all(|byte| matches!(byte, b' '..=b'~' | b'\n'))
        {
            return Err(SourcePrivateKeyError::ForbiddenCharacter);
        }
        let text = core::str::from_utf8(candidate)
            .map_err(|_| SourcePrivateKeyError::ForbiddenCharacter)?;

        let document = text.strip_suffix('\n').unwrap_or(text);
        let mut lines = document.split('\n');
        if lines.next() != Some(HEADER) {
            return Err(SourcePrivateKeyError::MissingHeader);
        }
        let body_and_footer: Vec<&str> = lines.collect();
        let (footer, body) = body_and_footer
            .split_last()
            .ok_or(SourcePrivateKeyError::MissingFooter)?;
        if *footer != FOOTER {
            return Err(SourcePrivateKeyError::MissingFooter);
        }
        check_body(body)?;

        Ok(Self(SecretString::new(format!("{document}\n"))))
    }

    /// The key document, ending in one line feed, for the one caller that writes
    /// it into the scratch file.
    ///
    /// The name is greppable on purpose: every read of the key is a line a
    /// reviewer can find with one search.
    #[must_use]
    pub fn expose(&self) -> &str {
        self.0.expose()
    }
}

/// Requires `lines` to be non-empty base64 lines that decode, joined, to a
/// document starting with the OpenSSH key magic.
///
/// # Errors
///
/// [`SourcePrivateKeyError::MalformedBody`] on an empty body, an empty or
/// over-long line, a character outside the base64 alphabet, a document that does
/// not decode, or one that lacks the magic.
fn check_body(lines: &[&str]) -> Result<(), SourcePrivateKeyError> {
    if lines.is_empty() {
        return Err(SourcePrivateKeyError::MalformedBody);
    }
    let mut joined = String::new();
    for line in lines {
        if line.is_empty() || line.len() > MAX_LINE_LENGTH {
            return Err(SourcePrivateKeyError::MalformedBody);
        }
        joined.push_str(line);
    }

    let decoded = STANDARD
        .decode(joined)
        .map_err(|_| SourcePrivateKeyError::MalformedBody)?;
    if decoded.starts_with(MAGIC) {
        Ok(())
    } else {
        Err(SourcePrivateKeyError::MalformedBody)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/secrets/source_private_key_tests.rs"]
mod tests;
