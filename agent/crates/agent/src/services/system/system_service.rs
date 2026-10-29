//! `SystemService`: the identity handshake. The panel asks it when it starts,
//! on every health and readiness request, and before it shows the backup root,
//! and refuses an agent whose contract revision lies outside the window it was
//! built for. It does not ask before each operation.

use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_distro::{DistroFamily, DistroInfo};
use tonic::{Request, Response, Status};

use crate::proto::system_service_server::SystemService;
use crate::proto::{
    AgentInfo, DistroFamily as WireFamily, GetAgentInfoRequest, GetAgentInfoResponse,
    get_agent_info_response,
};

/// Revision of `proto/agent/v1/` this binary is built from: the number the
/// panel compares against the window it drives (`AgentProtoVersionWindow` in
/// the backend; rules/proto.md "The contract revision").
///
/// Raised by one in the same change as every contract edit that
/// `maran proto --accept` records, and moved together with the panel's
/// window. `maran handshake` builds both from one tree and fails unless the
/// panel reports this agent `connected`, so a revision and a window moved
/// apart fail that gate. A contract edit that moves NEITHER is caught by the
/// agent's own test, which digests `contract-baseline.txt` against the newest
/// recorded revision and fails until this constant is raised.
///
/// 1 is every agent built before that policy: the constant stayed at 1
/// through every contract change until then, so a 1 names no contract and no
/// panel window contains it. 2 is the contract as of the change that
/// introduced the policy (issue #88).
const PROTO_VERSION: u32 = 2;

/// Answers "what agent is this, and what host is it on?".
pub struct SystemServiceImpl {
    /// Distribution detected once at startup.
    ///
    /// Detection reads a file that does not change while the process lives, and
    /// doing it per request would turn a handshake into disk I/O.
    distro: DistroInfo,
}

impl SystemServiceImpl {
    /// Creates the service around the startup-detected `distro`.
    #[must_use]
    pub fn new(distro: DistroInfo) -> Self {
        Self { distro }
    }

    /// Maps the internal family onto the wire enum.
    ///
    /// The two enums are kept separate on purpose: the wire one is a contract
    /// that must keep its numbers forever, the internal one is free to change.
    fn wire_family(&self) -> WireFamily {
        match self.distro.family {
            DistroFamily::Debian => WireFamily::Debian,
            DistroFamily::Rhel => WireFamily::Rhel,
        }
    }
}

#[tonic::async_trait]
impl SystemService for SystemServiceImpl {
    /// Returns the agent's version and the host's identity.
    ///
    /// Infallible by design: an agent that cannot answer this cannot have
    /// started, since the distribution is detected before the socket is bound.
    ///
    /// `backup_root` is reported from [`LocalBackupRoot::default`], which is
    /// built from `AgentPaths::BACKUP_ROOT` — the same constant every backup
    /// operation writes under. It is READ from here rather than restated by
    /// the panel, so there is one statement of the directory and the panel can
    /// only show what this answer said.
    async fn get_agent_info(
        &self,
        _request: Request<GetAgentInfoRequest>,
    ) -> Result<Response<GetAgentInfoResponse>, Status> {
        let info = AgentInfo {
            version: env!("CARGO_PKG_VERSION").to_owned(),
            distro_id: self.distro.id.clone(),
            family: self.wire_family() as i32,
            proto_version: PROTO_VERSION,
            backup_root: LocalBackupRoot::default().as_str().to_owned(),
        };

        Ok(Response::new(GetAgentInfoResponse {
            result: Some(get_agent_info_response::Result::Ok(info)),
        }))
    }
}

#[cfg(test)]
#[path = "../../tests/services/system/system_service_tests.rs"]
mod tests;
