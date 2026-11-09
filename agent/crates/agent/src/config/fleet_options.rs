//! Where the agent's fleet listener binds and where its identity lives.

use std::path::{Path, PathBuf};

use maran_agent_core::validation::fleet::listen_address::ListenAddress;

/// The two values that turn the fleet listener on (issue #180).
///
/// A struct that exists only when both were given, held as an `Option` on
/// [`super::agent_options::AgentOptions`]: there is no half-configured state to forget to check,
/// and a plain panel-local agent is the absence of this type rather than a flag in it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FleetOptions {
    /// The address and port the listener binds.
    pub listen: ListenAddress,
    /// The directory holding `node.key`, `node.pem` and the pinned `panel.pem`. Absolute.
    pub directory: PathBuf,
}

impl FleetOptions {
    /// The identity directory as a borrowed path.
    #[must_use]
    pub fn directory(&self) -> &Path {
        &self.directory
    }
}
