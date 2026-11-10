//! A source's SSH host key in the `<algorithm> <base64>` form a `known_hosts`
//! line carries.

use base64::Engine as _;
use base64::engine::general_purpose::STANDARD;

use super::host_key_error::HostKeyError;

/// The accepted algorithm names, and nothing else. `ssh-dss` is absent on
/// purpose: OpenSSH itself has disabled it by default since 7.0.
const ACCEPTED_ALGORITHMS: &[&str] = &[
    "ssh-ed25519",
    "ecdsa-sha2-nistp256",
    "ecdsa-sha2-nistp384",
    "ecdsa-sha2-nistp521",
    "ssh-rsa",
];

/// The most base64 characters a key may take. A 16384-bit RSA host key is under
/// 3 KiB of base64; no host key an administrator would pin comes near this, and
/// the ceiling is what bounds the file the agent writes from it.
const MAX_BASE64_LENGTH: usize = 2048;

/// The most characters the whole `<algorithm> <base64>` text may take: the
/// longest accepted algorithm name, the one separator and the base64 ceiling.
const MAX_LENGTH: usize = 19 + 1 + MAX_BASE64_LENGTH;

/// A host key an administrator may pin for a source: an allow-listed algorithm
/// and a base64 blob that really is a key of that algorithm.
///
/// It is the value the agent writes into the one-line `known_hosts` file that
/// `ssh` is told to trust with `StrictHostKeyChecking=yes`, so the grammar is
/// closed: exactly two fields separated by one space, no comment, no option, no
/// hostname field (the agent writes that itself), one line, no control
/// character. The blob is decoded and must start with the same algorithm name
/// the first field declares, so the text cannot name one algorithm and carry
/// another.
///
/// What it does not establish is that the key belongs to the host the
/// administrator thinks it does; that rests on the fingerprint comparison the
/// administrator made against the source's own console.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct HostKey {
    /// The allow-listed algorithm name, as written in the first field.
    algorithm: &'static str,
    /// The `<algorithm> <base64>` text exactly as accepted.
    text: String,
    /// The decoded key blob, which the fingerprint is the SHA-256 digest of.
    blob: Vec<u8>,
}

impl HostKey {
    /// Parses `candidate` as `<algorithm> <base64>`.
    ///
    /// # Errors
    ///
    /// - [`HostKeyError::Empty`] when `candidate` is empty.
    /// - [`HostKeyError::TooLong`] when it exceeds the length ceiling.
    /// - [`HostKeyError::ControlCharacter`] when any control character appears,
    ///   checked before the text is split.
    /// - [`HostKeyError::NotTwoFields`] when it is not exactly two fields
    ///   separated by one space (a comment, an option, a leading or trailing
    ///   space, or a second space).
    /// - [`HostKeyError::UnsupportedAlgorithm`] when the first field is not on
    ///   the allow-list.
    /// - [`HostKeyError::InvalidEncoding`] when the second field is empty or is
    ///   not standard base64.
    /// - [`HostKeyError::Malformed`] when the decoded key is too short to carry
    ///   an algorithm name.
    /// - [`HostKeyError::AlgorithmMismatch`] when the decoded key names another
    ///   algorithm.
    pub fn parse(candidate: &str) -> Result<Self, HostKeyError> {
        if candidate.is_empty() {
            return Err(HostKeyError::Empty);
        }
        if candidate.len() > MAX_LENGTH {
            return Err(HostKeyError::TooLong);
        }
        // Before any split: a newline must never get the chance to be read as a
        // field boundary or as the start of a second line.
        if candidate.chars().any(char::is_control) {
            return Err(HostKeyError::ControlCharacter);
        }

        let (declared, data) = candidate
            .split_once(' ')
            .ok_or(HostKeyError::NotTwoFields)?;
        if data.chars().any(char::is_whitespace) {
            return Err(HostKeyError::NotTwoFields);
        }
        let algorithm = ACCEPTED_ALGORITHMS
            .iter()
            .copied()
            .find(|accepted| *accepted == declared)
            .ok_or(HostKeyError::UnsupportedAlgorithm)?;
        if data.is_empty() || data.len() > MAX_BASE64_LENGTH {
            return Err(HostKeyError::InvalidEncoding);
        }

        let blob = STANDARD
            .decode(data)
            .map_err(|_| HostKeyError::InvalidEncoding)?;
        check_names_algorithm(&blob, algorithm)?;

        Ok(Self {
            algorithm,
            text: candidate.to_owned(),
            blob,
        })
    }

    /// The algorithm name, one of the five accepted.
    #[must_use]
    pub fn algorithm(&self) -> &'static str {
        self.algorithm
    }

    /// The `<algorithm> <base64>` text, ready to follow a hostname field.
    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.text
    }

    /// The decoded key blob: what `ssh` hashes to print the key's fingerprint.
    #[must_use]
    pub fn blob(&self) -> &[u8] {
        &self.blob
    }
}

/// Requires the blob's leading SSH wire-format string (RFC 4251 section 5) to be
/// `algorithm`.
///
/// # Errors
///
/// [`HostKeyError::Malformed`] when the blob is too short to hold the length and
/// the name it promises; [`HostKeyError::AlgorithmMismatch`] when the name
/// differs.
fn check_names_algorithm(blob: &[u8], algorithm: &str) -> Result<(), HostKeyError> {
    let (length, rest) = blob
        .split_first_chunk::<4>()
        .ok_or(HostKeyError::Malformed)?;
    let length =
        usize::try_from(u32::from_be_bytes(*length)).map_err(|_| HostKeyError::Malformed)?;
    let name = rest.get(..length).ok_or(HostKeyError::Malformed)?;
    if name == algorithm.as_bytes() {
        Ok(())
    } else {
        Err(HostKeyError::AlgorithmMismatch)
    }
}

#[cfg(test)]
#[path = "../../tests/validation/web/host_key_tests.rs"]
mod tests;
