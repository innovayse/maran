//! Tests for the identity handshake.
//!
//! Two propositions live here. The first is the contract revision: the
//! handshake reports the revision of `proto/agent/v1/`, and a change to the
//! recorded contract baseline cannot land without raising it (the digest test
//! below; rules/proto.md "The contract revision").
//!
//! The second, older one: the handshake REPORTS the directory
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

#[tokio::test]
async fn the_handshake_reports_the_current_contract_revision() {
    let response = service()
        .get_agent_info(Request::new(GetAgentInfoRequest {}))
        .await
        .unwrap()
        .into_inner();

    let get_agent_info_response::Result::Ok(info) = response.result.unwrap() else {
        panic!("the handshake is infallible");
    };

    // Revision 1 is every agent built before the revision policy existed, and
    // no panel window contains it; revision 2 is the contract as of the change
    // that introduced the policy. The panel's `AgentProtoVersionWindow` moves
    // with this number (rules/proto.md "The contract revision"), so a literal
    // here is the point: changing the constant must be a deliberate edit of
    // this test too, in the same change as the window.
    assert_eq!(info.proto_version, 2);
}

#[tokio::test]
async fn the_reported_revision_is_never_zero() {
    let response = service()
        .get_agent_info(Request::new(GetAgentInfoRequest {}))
        .await
        .unwrap()
        .into_inner();

    let get_agent_info_response::Result::Ok(info) = response.result.unwrap() else {
        panic!("the handshake is infallible");
    };

    // Zero is what the wire reads from a sender that never set the field, and
    // it lies outside every panel window.
    assert_ne!(info.proto_version, 0);
}

/// The contract baseline `maran proto --accept` records, read as the bytes the
/// reviewer sees in the diff.
const CONTRACT_BASELINE: &str =
    include_str!("../../../../../../../proto/agent/v1/contract-baseline.txt");

/// Every revision of the contract and the digest of the baseline that revision
/// was recorded with, oldest first. Revision 1 names no contract and has no
/// row. A contract change appends a row; it never edits one.
const REVISIONS: &[(u32, u64)] = &[(2, 0xa38e_f92b_0c0b_8c2b)];

/// FNV-1a over the bytes: a fixed, dependency-free digest. Its job is to
/// notice that the baseline changed, not to resist anyone.
fn digest(bytes: &[u8]) -> u64 {
    bytes.iter().fold(0xcbf2_9ce4_8422_2325_u64, |hash, byte| {
        (hash ^ u64::from(*byte)).wrapping_mul(0x0100_0000_01b3)
    })
}

#[test]
fn a_contract_baseline_change_comes_with_a_proto_version_bump() {
    let (revision, recorded) = *REVISIONS.last().unwrap();

    // The constant and the newest row move together, or this names the miss.
    assert_eq!(
        super::PROTO_VERSION,
        revision,
        "PROTO_VERSION must equal the newest row of REVISIONS"
    );
    // A baseline that no longer matches its row is a contract edit whose
    // revision was not raised: bump PROTO_VERSION, append a row with the new
    // digest, and move the panel's window (rules/proto.md "The contract revision").
    assert_eq!(
        digest(CONTRACT_BASELINE.as_bytes()),
        recorded,
        "proto/agent/v1/contract-baseline.txt changed without a PROTO_VERSION bump"
    );
}

#[test]
fn the_revision_digest_notices_a_one_byte_change() {
    // Inverse control: the digest must move when the baseline does, or the
    // test above would pass whatever the file held.
    let mut changed = CONTRACT_BASELINE.to_owned();
    changed.push(' ');

    assert_ne!(
        digest(changed.as_bytes()),
        digest(CONTRACT_BASELINE.as_bytes())
    );
}
