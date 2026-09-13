//! Tests for the identity handshake.
//!
//! The proposition this file exists for: the handshake REPORTS the directory
//! backups are written into, and reports the agent's own constant rather than
//! any other spelling of it. The panel has no other way to learn the path, and
//! it now shows what this answer says — so an answer that named a directory the
//! backup operations do not use would put a wrong path in front of an operator
//! during a recovery, which is the one moment the path is read.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_distro::{DistroFamily, DistroInfo};
use tonic::Request;

use super::SystemServiceImpl;
use crate::proto::system_service_server::SystemService;
use crate::proto::{GetAgentInfoRequest, get_agent_info_response};

/// The service over a detected distribution, which the handshake does not read.
fn service() -> SystemServiceImpl {
    SystemServiceImpl::new(DistroInfo {
        id: "ubuntu".to_owned(),
        version_id: "24.04".to_owned(),
        family: DistroFamily::Debian,
    })
}

#[tokio::test]
async fn the_handshake_reports_the_agents_own_backup_root() {
    let response = service()
        .get_agent_info(Request::new(GetAgentInfoRequest {}))
        .await
        .unwrap()
        .into_inner();

    let get_agent_info_response::Result::Ok(info) = response.result.unwrap() else {
        panic!("the handshake is infallible");
    };

    assert_eq!(info.backup_root, AgentPaths::BACKUP_ROOT);
}

#[tokio::test]
async fn the_reported_backup_root_is_never_empty() {
    let response = service()
        .get_agent_info(Request::new(GetAgentInfoRequest {}))
        .await
        .unwrap()
        .into_inner();

    let get_agent_info_response::Result::Ok(info) = response.result.unwrap() else {
        panic!("the handshake is infallible");
    };

    // Empty is the wire's "this agent predates the field", which makes the
    // panel say it does not know the path. An agent that carries the field must
    // never send that.
    assert!(!info.backup_root.is_empty());
}
