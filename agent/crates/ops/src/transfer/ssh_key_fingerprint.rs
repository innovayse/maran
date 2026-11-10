//! The fingerprint `ssh` prints for a key, computed here.

use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;
use sha2::{Digest, Sha256};

/// The `SHA256:<base64>` fingerprint of `key`, as `ssh-keygen -l` prints it: the
/// SHA-256 of the decoded key blob, in base64 without padding.
///
/// Computing it here, from the validated key the agent will pin, rather than
/// reading a fingerprint the source or a tool printed, means the figure an
/// administrator compares is derived from the very bytes that get trusted, and
/// costs no extra program run. It cannot fail: a [`HostKey`] already holds its
/// decoded blob.
#[must_use]
pub fn ssh_key_fingerprint(key: &HostKey) -> HostKeyFingerprint {
    HostKeyFingerprint::from_digest(&Sha256::digest(key.blob()).into())
}

#[cfg(test)]
#[path = "../tests/transfer/ssh_key_fingerprint_tests.rs"]
mod tests;
