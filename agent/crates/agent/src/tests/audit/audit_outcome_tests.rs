//! Tests for `audit_outcome`: the outcome field spells only closed text.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use tonic::Code;

use super::AuditOutcome;
use crate::proto::ErrorCode;

#[test]
fn ok_renders_as_ok() {
    assert_eq!(AuditOutcome::Ok.to_string(), "ok");
}

#[test]
fn a_refusal_renders_as_the_proto_name_of_its_code() {
    assert_eq!(
        AuditOutcome::Refused(ErrorCode::InvalidInput).to_string(),
        "ERROR_CODE_INVALID_INPUT"
    );
    assert_eq!(
        AuditOutcome::Refused(ErrorCode::NotFound).to_string(),
        "ERROR_CODE_NOT_FOUND"
    );
}

#[test]
fn an_unsettled_rpc_renders_as_unset() {
    assert_eq!(AuditOutcome::Unset.to_string(), "unset");
}

#[test]
fn a_transport_failure_renders_with_the_fixed_name_of_the_status() {
    assert_eq!(
        AuditOutcome::Transport(Code::PermissionDenied).to_string(),
        "transport:PermissionDenied"
    );
    assert_eq!(
        AuditOutcome::Transport(Code::Internal).to_string(),
        "transport:Internal"
    );
}

#[test]
fn every_transport_name_is_one_token_without_separators() {
    let codes = [
        Code::Ok,
        Code::Cancelled,
        Code::Unknown,
        Code::InvalidArgument,
        Code::DeadlineExceeded,
        Code::NotFound,
        Code::AlreadyExists,
        Code::PermissionDenied,
        Code::ResourceExhausted,
        Code::FailedPrecondition,
        Code::Aborted,
        Code::OutOfRange,
        Code::Unimplemented,
        Code::Internal,
        Code::Unavailable,
        Code::DataLoss,
        Code::Unauthenticated,
    ];
    assert_eq!(codes.len(), 17, "the table covers every gRPC status");
    for code in codes {
        let text = AuditOutcome::Transport(code).to_string();
        assert!(text.starts_with("transport:"));
        assert!(
            text.chars().all(|c| c.is_ascii_alphanumeric() || c == ':'),
            "{text}"
        );
    }
}
