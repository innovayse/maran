//! Tests for running a source operation off the async workers.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::TransferOpError;

use super::run_source_call;
use crate::proto::{ErrorCode, SourceFailureReason};

#[tokio::test]
async fn a_successful_operation_returns_its_value() {
    let value = run_source_call(|| Ok::<_, TransferOpError>(41 + 1))
        .await
        .unwrap();

    assert_eq!(value, 42);
}

#[tokio::test]
async fn a_failed_operation_keeps_its_error_and_its_connection_reason() {
    let failure = run_source_call(|| Err::<(), _>(TransferOpError::HostKeyChanged))
        .await
        .unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::ValidationFailed as i32);
    assert_eq!(failure.reason, SourceFailureReason::HostKeyChanged);
}

#[tokio::test]
async fn a_fault_of_this_host_keeps_no_reason() {
    let failure = run_source_call(|| Err::<(), _>(TransferOpError::ScratchUnavailable))
        .await
        .unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(failure.reason, SourceFailureReason::Unspecified);
}

#[tokio::test]
async fn a_panicking_task_is_a_system_failure_and_never_a_reason() {
    let failure = run_source_call(|| -> Result<(), TransferOpError> { panic!("task bug") })
        .await
        .unwrap_err();

    assert_eq!(failure.error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(failure.reason, SourceFailureReason::Unspecified);
}
