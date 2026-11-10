//! What a `PullAccountFiles` request must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::model::pull_phase::PullPhase;

use super::validated_pull_request;
use crate::proto::{self, ErrorCode, PullAccountFilesRequest, SourceFailureReason};

/// An inert key document that opens nothing anywhere.
const KEY: &str = "-----BEGIN OPENSSH PRIVATE KEY-----\n\
    b3BlbnNzaC1rZXktdjEACQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ\n\
    -----END OPENSSH PRIVATE KEY-----\n";

/// An inert host key.
const HOST_KEY: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

/// A request every field of which is valid; each test breaks one.
fn valid() -> PullAccountFilesRequest {
    PullAccountFilesRequest {
        endpoint: Some(proto::SourceEndpoint {
            host: "192.0.2.10".to_owned(),
            port: 22,
            user: "root".to_owned(),
        }),
        credential: Some(proto::SourceCredential {
            private_key: KEY.as_bytes().to_vec(),
            host_key: HOST_KEY.to_owned(),
        }),
        account_username: "alice".to_owned(),
        source_home: "/home/alice".to_owned(),
        phase: proto::PullPhase::Initial as i32,
        bandwidth_limit_kib: 0,
        expected_bytes: 1024,
    }
}

#[test]
fn a_valid_request_is_yielded_with_no_limit_for_zero() {
    let pull = validated_pull_request(valid()).unwrap();

    assert_eq!(pull.account.as_str(), "alice");
    assert_eq!(pull.phase, PullPhase::Initial);
    assert!(pull.bandwidth.is_none());
    assert_eq!(pull.expected_bytes, 1024);
}

#[test]
fn a_limit_is_yielded_when_given() {
    let mut request = valid();
    request.bandwidth_limit_kib = 4096;

    let pull = validated_pull_request(request).unwrap();

    assert_eq!(pull.bandwidth.unwrap().kibibytes_per_second(), 4096);
}

#[test]
fn the_final_phase_is_read_as_the_final_phase() {
    let mut request = valid();
    request.phase = proto::PullPhase::Final as i32;

    assert_eq!(
        validated_pull_request(request).unwrap().phase,
        PullPhase::Final
    );
}

#[test]
fn an_unspecified_or_unknown_phase_is_refused_never_defaulted() {
    // The final phase deletes, so a missing phase must never become one.
    for phase in [proto::PullPhase::Unspecified as i32, 99] {
        let mut request = valid();
        request.phase = phase;

        let failure = validated_pull_request(request).unwrap_err();

        assert_eq!(
            failure.error.code,
            ErrorCode::InvalidInput as i32,
            "phase {phase}"
        );
    }
}

#[test]
fn a_home_that_is_not_a_remote_path_is_refused() {
    for home in [
        "",
        "home/alice",
        "/home/../etc",
        "/home/a b",
        "/home/a;b",
        "-x",
    ] {
        let mut request = valid();
        request.source_home = home.to_owned();

        let failure = validated_pull_request(request).unwrap_err();

        assert_eq!(
            failure.error.code,
            ErrorCode::InvalidInput as i32,
            "{home:?}"
        );
    }
}

#[test]
fn a_forbidden_address_is_refused_with_its_reason_first() {
    let mut request = valid();
    request.endpoint = Some(proto::SourceEndpoint {
        host: "127.0.0.1".to_owned(),
        port: 22,
        user: "root".to_owned(),
    });
    request.source_home = "garbage".to_owned();

    let failure = validated_pull_request(request).unwrap_err();

    assert_eq!(failure.reason, SourceFailureReason::AddressRefused);
}

#[test]
fn a_limit_above_the_ceiling_is_refused() {
    let mut request = valid();
    request.bandwidth_limit_kib = 2_000_000;

    let failure = validated_pull_request(request).unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32);
}
