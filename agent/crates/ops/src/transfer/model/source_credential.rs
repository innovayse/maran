//! What authenticates this agent to a source, and the key it must present.

use maran_agent_core::validation::secrets::source_private_key::SourcePrivateKey;
use maran_agent_core::validation::web::host_key::HostKey;

/// The private key of one registered source and its pinned host key.
///
/// The private half is a credential that can open a root shell on the source:
/// it is held for one call, written only to the root-only scratch directory
/// that is removed on every path, and printed by nothing, because
/// [`SourcePrivateKey`]'s `Debug` redacts it and this struct derives `Debug`
/// over it. There is no `Clone` and no `serde` implementation.
#[derive(Debug)]
pub struct SourceCredential {
    /// The private half `generate_source_key` minted.
    pub private_key: SourcePrivateKey,
    /// The host key the administrator confirmed; the only one a connection
    /// accepts.
    pub host_key: HostKey,
}
