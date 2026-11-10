//! Tests for the response a host key scan ends with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::host_key::HostKey;
use maran_agent_core::validation::web::host_key_fingerprint::HostKeyFingerprint;
use maran_ops::transfer::{ScannedHostKey, TransferOpError};

use super::reported_scanned_host_keys;
use crate::proto::{ErrorCode, SourceFailureReason, scan_host_key_response};
use crate::services::transfer::source_failure::SourceFailure;

/// An inert ed25519 host key.
const ED25519: &str =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcH";

#[test]
fn a_scan_that_found_keys_answers_them_with_no_reason() {
    let response = reported_scanned_host_keys(Ok(vec![ScannedHostKey {
        key: HostKey::parse(ED25519).unwrap(),
        fingerprint: HostKeyFingerprint::from_digest(&[3; 32]),
    }]));

    let Some(scan_host_key_response::Result::Ok(keys)) = response.result else {
        panic!("a scan that found keys answers ok");
    };
    assert_eq!(keys.keys.len(), 1);
    assert_eq!(
        response.failure_reason,
        SourceFailureReason::Unspecified as i32
    );
}

#[test]
fn a_failed_scan_answers_the_error_and_the_reason_together() {
    let response =
        reported_scanned_host_keys(Err(SourceFailure::of(&TransferOpError::Unreachable)));

    let Some(scan_host_key_response::Result::Error(error)) = response.result else {
        panic!("a failed scan answers the error");
    };
    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(
        response.failure_reason,
        SourceFailureReason::Unreachable as i32
    );
}
