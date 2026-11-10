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
use crate::proto::{DistroFamily as WireFamily, GetAgentInfoRequest, get_agent_info_response};

/// The service over a detected distribution, which the handshake does not read.
fn service() -> SystemServiceImpl {
    SystemServiceImpl::new(DistroInfo {
        id: "ubuntu".to_owned(),
        version_id: "24.04".to_owned(),
        family: DistroFamily::Debian,
    })
}

/// The wire family the handshake states for a host of `family`.
async fn stated_family(family: DistroFamily) -> i32 {
    let service = SystemServiceImpl::new(DistroInfo {
        id: "host".to_owned(),
        version_id: "1".to_owned(),
        family,
    });
    let response = service
        .get_agent_info(Request::new(GetAgentInfoRequest {}))
        .await
        .unwrap()
        .into_inner();

    let get_agent_info_response::Result::Ok(info) = response.result.unwrap() else {
        panic!("the handshake is infallible");
    };

    info.family
}

#[tokio::test]
async fn a_suse_host_is_answered_as_the_suse_wire_family() {
    assert_eq!(
        stated_family(DistroFamily::Suse).await,
        WireFamily::Suse as i32
    );
}

#[tokio::test]
async fn a_debian_host_is_answered_as_the_debian_wire_family() {
    assert_eq!(
        stated_family(DistroFamily::Debian).await,
        WireFamily::Debian as i32
    );
}

#[tokio::test]
async fn a_rhel_host_is_answered_as_the_rhel_wire_family() {
    assert_eq!(
        stated_family(DistroFamily::Rhel).await,
        WireFamily::Rhel as i32
    );
}

#[tokio::test]
async fn the_suse_wire_value_is_three_and_never_unspecified() {
    // The number is the contract: the panel's refusal of an unknown family
    // reads it, and zero is the value a sender that never set it would carry.
    assert_eq!(WireFamily::Suse as i32, 3);
    assert_ne!(
        stated_family(DistroFamily::Suse).await,
        WireFamily::Unspecified as i32
    );
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
    // that introduced the policy; revision 3 adds `InstallCertificate`'s
    // `refused` answer (issue #214); revision 4 adds the two package-source
    // error codes (issue #251); revision 5 adds the account filter of
    // `GetAccountsDiskUsage` (issue #196); revision 6 splits the root
    // filesystem into used, reserved and available (issue #77); revision 7
    // adds the free-space floor's error code and figure (issue #148); revision 8
    // adds `SetAccountResourceLimits` (issue #133); revision 9 adds the panel's own
    // certificate rpcs (issue #132); revision 10 adds `GetClockSync`
    // (issue #150); revision 11 adds `RunCronEntry` (issue #158); revision 12
    // adds `SetSftpAuthorizedKeys` (issue #135); revision 13 adds the
    // three-state account facts and the explicit ban term (issue #323); revision
    // 14 adds `ListInstallablePhpVersions` (issue #330); revision 15 adds
    // `EndSftpSessions` and `ERROR_CODE_AGENT_SATURATED` (issues #339 and #333);
    // revision 16 adds `ERROR_CODE_RUN_NOT_STOPPED` (issue #336); revision 17 is
    // the contract before backup verification; revision 18 adds `VerifyBackup`
    // (issue #134); revision 19 adds the update classification and
    // `GetRebootRequirement` (issue #163); revision 20 adds
    // `RestorePanelSelfSigned` (issue #425); revision 21 is the one combined
    // revision for the v1 feature contract growth (dns, mail and transfer
    // services, database logins, site options, PHP extensions, file manager,
    // account limits, backup keys and four error codes); revision 24 adds the
    // remote-source rpcs of `transfer.proto` (issue #197); revision 25 adds
    // `DISTRO_FAMILY_SUSE` (issue #59); revision 26 adds the transfer half of server
    // migration (issue #197); revision 27 is the newest row of `REVISIONS` below.
    // The panel's
    // `AgentProtoVersionWindow` moves
    // with this number (rules/proto.md "The contract revision"), so a literal
    // here is the point: changing the constant must be a deliberate edit of
    // this test too, in the same change as the window.
    assert_eq!(info.proto_version, 27);
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
const REVISIONS: &[(u32, u64)] = &[
    (2, 0xa38e_f92b_0c0b_8c2b),
    (3, 0xb880_37f8_ad50_ac7e),
    (4, 0x3db3_ea7e_ab61_b6bb),
    (5, 0x05c6_6b26_aeba_4725),
    (6, 0x9a63_33b9_4199_bdc7),
    (7, 0xb283_18aa_a02e_4f4e),
    (8, 0xd31a_75ce_b42d_6396),
    (9, 0x5e80_6a10_6744_3e8c),
    (10, 0x6158_aa3d_0a72_3aca),
    (11, 0xdb27_e25a_498b_4086),
    (12, 0xaee3_8cde_9457_4a26),
    (13, 0xc265_007f_e9ed_3bad),
    (14, 0xe775_6ebf_86a6_74ca),
    (15, 0x79fb_aa5c_2e22_2194),
    (16, 0x6d9e_fca8_8145_5caa),
    (17, 0x8462_2503_bd18_b072),
    (18, 0xcc07_f6a6_861a_d2a4),
    (19, 0x0cc4_b43b_c23f_52ff),
    (20, 0x013b_52f9_27db_295b),
    (21, 0x4057_c8a8_c560_0f2f),
    (22, 0xddcb_2ce0_eaba_d62b),
    (23, 0x2d29_e8e1_7096_e19e),
    (24, 0x6470_ff3f_530a_3df1),
    (25, 0x195c_9d0e_a6d5_2dc6),
    (26, 0xe680_e3cf_639d_2959),
    (27, 0xf8dd_e61c_e542_4f11),
];

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
