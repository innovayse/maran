//! What an authenticated source rpc must carry to be served.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;
use crate::proto::{ErrorCode, SourceFailureReason};

/// An inert key document that opens nothing anywhere.
const KEY: &str = "-----BEGIN OPENSSH PRIVATE KEY-----\n\
    b3BlbnNzaC1rZXktdjEACQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ\n\
    -----END OPENSSH PRIVATE KEY-----\n";

/// An inert host key.
const HOST_KEY: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

fn endpoint(host: &str) -> Option<proto::SourceEndpoint> {
    Some(proto::SourceEndpoint {
        host: host.to_owned(),
        port: 22,
        user: "root".to_owned(),
    })
}

fn credential(private_key: &str) -> Option<proto::SourceCredential> {
    Some(proto::SourceCredential {
        private_key: private_key.as_bytes().to_vec(),
        host_key: HOST_KEY.to_owned(),
    })
}

#[test]
fn a_valid_endpoint_and_credential_are_both_yielded() {
    let (endpoint, credential) =
        validated_source_inputs(endpoint("192.0.2.10"), credential(KEY)).unwrap();

    assert_eq!(endpoint.host.as_str(), "192.0.2.10");
    assert_eq!(credential.host_key.algorithm(), "ssh-ed25519");
}

#[test]
fn a_forbidden_address_is_refused_with_its_reason_before_the_credential_is_read() {
    // The credential is garbage too: the endpoint is looked at first, so the
    // refusal the panel sees is the address one.
    let failure =
        validated_source_inputs(endpoint("127.0.0.1"), credential("garbage")).unwrap_err();

    assert_eq!(failure.reason, SourceFailureReason::AddressRefused);
}

#[test]
fn a_bad_credential_is_invalid_input_with_no_connection_reason() {
    let failure =
        validated_source_inputs(endpoint("192.0.2.10"), credential("garbage")).unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32);
    assert_eq!(failure.reason, SourceFailureReason::Unspecified);
}

#[test]
fn an_absent_credential_is_invalid_input() {
    let failure = validated_source_inputs(endpoint("192.0.2.10"), None).unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::InvalidInput as i32);
}
