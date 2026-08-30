//! `SystemService`: the identity handshake the API performs before trusting the
//! agent.

use maran_distro::{DistroFamily, DistroInfo};
use tonic::{Request, Response, Status};

use crate::proto::system_service_server::SystemService;
use crate::proto::{
    AgentInfo, DistroFamily as WireFamily, GetAgentInfoRequest, GetAgentInfoResponse,
    get_agent_info_response,
};

/// Contract revision this binary implements; bumped once per additive release.
const PROTO_VERSION: u32 = 1;

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
    async fn get_agent_info(
        &self,
        _request: Request<GetAgentInfoRequest>,
    ) -> Result<Response<GetAgentInfoResponse>, Status> {
        let info = AgentInfo {
            version: env!("CARGO_PKG_VERSION").to_owned(),
            distro_id: self.distro.id.clone(),
            family: self.wire_family() as i32,
            proto_version: PROTO_VERSION,
        };

        Ok(Response::new(GetAgentInfoResponse {
            result: Some(get_agent_info_response::Result::Ok(info)),
        }))
    }
}
