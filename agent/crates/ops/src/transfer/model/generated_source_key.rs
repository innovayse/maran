//! One freshly generated key pair for a source.

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;

/// An ed25519 key pair minted for one source.
///
/// The private half is the credential the panel stores encrypted and sends back
/// on every later call; the agent keeps no copy. It derives `Debug` over a type
/// that prints nothing of it.
#[derive(Debug)]
pub struct GeneratedSourceKey {
    /// The public half: algorithm and key, no option and no comment (the panel
    /// adds both when it shows the line the operator pastes).
    pub public_key: SshPublicKey,
    /// The private half in the OpenSSH private key format.
    pub private_key: SourcePrivateKey,
    /// The `SHA256:` fingerprint of the public half, in the form `ssh` prints.
    /// It uses the same grammar as a host key's fingerprint and is computed the
    /// same way; it names OUR key, not the source's.
    pub fingerprint: HostKeyFingerprint,
}
