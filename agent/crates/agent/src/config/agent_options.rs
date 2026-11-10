//! Command-line options of the agent process.

use std::path::{Path, PathBuf};

use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::web::panel_host_names::PanelHostNames;

use super::fleet_options::FleetOptions;

/// Default production socket path (spec §9).
pub(super) const DEFAULT_SOCKET: &str = "/run/maran/agent.sock";

/// Flag selecting the socket to bind.
pub(super) const SOCKET_FLAG: &str = "--socket";

/// Flag selecting the single uid allowed to connect.
pub(super) const ALLOW_UID_FLAG: &str = "--allow-uid";

/// Flag setting the free-space floor, in mebibytes (issue #148).
pub(super) const FREE_SPACE_FLOOR_FLAG: &str = "--free-space-floor-mib";

/// Flag giving the address and port of the fleet listener (issue #180).
pub(super) const FLEET_LISTEN_FLAG: &str = "--fleet-listen";

/// Flag naming the directory that holds this node's fleet identity and the pinned panel
/// certificate (issue #180).
pub(super) const FLEET_DIR_FLAG: &str = "--fleet-dir";

/// Flag giving the host names the panel itself is served under, as `--reserved-host-names=<a>,<b>`.
///
/// The value is glued to the flag with `=` on purpose: the unit file passes it as
/// `--reserved-host-names=${MARAN_AGENT_RESERVED_HOST_NAMES}`, and a host with no recorded name
/// must still produce ONE argument. As a separate word, an empty variable would leave the flag
/// dangling and the agent would refuse to start.
pub(super) const RESERVED_HOST_NAMES_FLAG: &str = "--reserved-host-names";

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
    /// The names the panel itself is served under, which no site, alias, certificate or zone
    /// may take. Empty by default, which refuses nothing beyond the fixed reserved set.
    pub panel_host_names: PanelHostNames,
    /// The fleet listener, present only when BOTH `--fleet-listen` and `--fleet-dir` were given.
    ///
    /// `None` is the default and means a panel-local agent: the process serves the unix socket
    /// and opens no network port (rules/security.md item 10).
    pub fleet: Option<FleetOptions>,
}

impl AgentOptions {
    /// The socket path as a borrowed path.
    #[must_use]
    pub fn socket_path(&self) -> &Path {
        &self.socket_path
    }
}
