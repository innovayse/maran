//! Tests for the response a connection test ends with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::transfer::{SourceKind, TestedSource, TransferOpError};

use super::reported_tested_source;
use crate::proto::{ErrorCode, SourceFailureReason, test_source_response};
use crate::services::transfer::source_failure::SourceFailure;

#[test]
fn a_test_that_ran_answers_the_observations_with_no_reason() {
    let response = reported_tested_source(Ok(TestedSource {
        is_root: true,
        home_disk: None,
        system_release: None,
        kind: SourceKind::GenericLinux,
        maran_version: None,
        database_server: None,
        web_server: None,
        mail_found: false,
    }));

    let Some(test_source_response::Result::Ok(tested)) = response.result else {
        panic!("a test that ran answers ok");
    };
    assert!(tested.is_root);
    assert_eq!(
        response.failure_reason,
        SourceFailureReason::Unspecified as i32
    );
}

#[test]
fn each_connection_failure_travels_with_its_own_reason() {
    for (failure, reason) in [
        (
            TransferOpError::HostKeyChanged,
            SourceFailureReason::HostKeyChanged,
        ),
        (
            TransferOpError::AuthRefused,
            SourceFailureReason::AuthRefused,
        ),
        (
            TransferOpError::Unreachable,
            SourceFailureReason::Unreachable,
        ),
        (
            TransferOpError::AddressRefused,
            SourceFailureReason::AddressRefused,
        ),
    ] {
        let response = reported_tested_source(Err(SourceFailure::of(&failure)));

        assert!(matches!(
            response.result,
            Some(test_source_response::Result::Error(_))
        ));
        assert_eq!(response.failure_reason, reason as i32, "{failure}");
    }
}

#[test]
fn a_fault_of_this_host_travels_with_no_reason() {
    let response =
        reported_tested_source(Err(SourceFailure::of(&TransferOpError::ScratchUnavailable)));

    let Some(test_source_response::Result::Error(error)) = response.result else {
        panic!("a fault answers the error");
    };
    assert_eq!(error.code, ErrorCode::SystemFailure as i32);
    assert_eq!(
        response.failure_reason,
        SourceFailureReason::Unspecified as i32
    );
}
