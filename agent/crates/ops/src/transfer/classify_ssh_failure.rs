//! Tells apart why an `ssh` connection ended, from the client's own words.

use crate::transfer::transfer_op_error::TransferOpError;

/// Phrases `ssh` prints when the host key it was shown is not the one it was
/// told to require: a different key, no key of the pinned algorithm, or no
/// pinned entry at all. All three are the same fact to the panel, a host that
/// is not the one the administrator confirmed.
const HOST_KEY_PHRASES: [&str; 4] = [
    "host key verification failed",
    "host identification has changed",
    "host key is known",
    "no matching host key type",
];

/// Phrases `ssh` prints when the source accepted the connection and refused the
/// key.
const AUTH_PHRASES: [&str; 3] = [
    "permission denied",
    "too many authentication failures",
    "no supported authentication methods",
];

/// The error for an `ssh` run that exited with status 255, decided from what the
/// CLIENT printed on its standard error (in the `C` locale the spawn core sets).
///
/// A host-key phrase wins over an authentication one, because a connection that
/// failed the host check never reached authentication. Anything else is
/// [`TransferOpError::Unreachable`]: the handshake did not complete (refused,
/// filtered, timed out, closed). The text is read, never kept: it is matched
/// against fixed phrases and dropped, so nothing the source sent can reach an
/// error value, and a remote command that happened to exit 255 is read as the
/// same unreachable source rather than trusted.
#[must_use]
pub fn classify_ssh_failure(stderr: &str) -> TransferOpError {
    let lowered = stderr.to_ascii_lowercase();

    if HOST_KEY_PHRASES
        .iter()
        .any(|phrase| lowered.contains(phrase))
    {
        return TransferOpError::HostKeyChanged;
    }

    if AUTH_PHRASES.iter().any(|phrase| lowered.contains(phrase)) {
        return TransferOpError::AuthRefused;
    }

    TransferOpError::Unreachable
}

#[cfg(test)]
#[path = "../tests/transfer/classify_ssh_failure_tests.rs"]
mod tests;
