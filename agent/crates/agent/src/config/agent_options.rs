//! Command-line options of the agent process.

use std::path::{Path, PathBuf};

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;

/// Default production socket path (spec §9).
pub(super) const DEFAULT_SOCKET: &str = "/run/maran/agent.sock";

/// Flag selecting the socket to bind.
pub(super) const SOCKET_FLAG: &str = "--socket";

/// Flag selecting the single uid allowed to connect.
pub(super) const ALLOW_UID_FLAG: &str = "--allow-uid";

/// Flag setting the free-space floor, in mebibytes (issue #148).
pub(super) const FREE_SPACE_FLOOR_FLAG: &str = "--free-space-floor-mib";

/// How the agent was asked to run.
///
/// Built by [`super::invocation::Invocation::parse`], which is where a command
/// line is turned into one of these — the type itself only carries the answer.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AgentOptions {
    /// Path of the unix socket to bind.
    pub socket_path: PathBuf,
    /// The uid permitted to use the agent.
    pub allow_uid: u32,
    /// The least room a filesystem must still have before an operation that
    /// writes into it is started.
    pub free_space_floor: FreeSpaceFloor,
}

impl AgentOptions {
    /// The socket path as a borrowed path.
    #[must_use]
    pub fn socket_path(&self) -> &Path {
        &self.socket_path
    }
}
