//! One host key a source offered.

use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;

/// A host key read from a source, with the fingerprint an administrator
/// compares against the source's own console.
///
/// The algorithm is `key.algorithm()` and is not repeated: one field, one
/// meaning. The fingerprint is COMPUTED by this agent from the key it holds
/// (`ssh_key_fingerprint`), never copied from a line the source sent.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ScannedHostKey {
    /// The key, as the `<algorithm> <base64>` pair a known_hosts line carries.
    pub key: HostKey,
    /// The `SHA256:<base64>` fingerprint of that key.
    pub fingerprint: HostKeyFingerprint,
}
