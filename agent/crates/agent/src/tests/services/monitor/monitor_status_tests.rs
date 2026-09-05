//! Tests for the wire code every monitoring failure travels as.
//!
//! Every variant of `MonitorError` is a system failure, and the tests say why
//! that short list is right rather than lazy: this area accepts no input, so
//! there is no INVALID_INPUT to map, and a unit that is down or absent is
//! reported as a STATE rather than as a failure at all.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::monitor::MonitorError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// Every variant this area can produce.
fn every_variant() -> Vec<MonitorError> {
    vec![
        MonitorError::HostStatisticsUnavailable,
        MonitorError::FilesystemUnavailable,
        MonitorError::ServiceManagerUnavailable { code: 4 },
        MonitorError::AccountsUnavailable,
    ]
}

#[test]
fn every_reading_that_failed_is_reported_as_a_fault_of_the_host() {
    // None of the three rpcs carries a field, so nothing a caller sent could
    // have caused any of these. Reporting one as INVALID_INPUT would send an
    // operator to change a request that has nothing in it.
    for error in every_variant() {
        assert_eq!(
            to_agent_error(&error).code,
            ErrorCode::SystemFailure as i32,
            "{error:?}"
        );
    }
}

#[test]
fn a_service_manager_that_refused_the_query_reports_its_status() {
    // The status is the whole payload this area's errors carry, and it is what
    // separates "the service manager is not running" from "it answered
    // something we could not use".
    let error = to_agent_error(&MonitorError::ServiceManagerUnavailable { code: 4 });

    assert!(
        error.message.contains('4'),
        "an operator needs the tool's own status: {}",
        error.message
    );
}

#[test]
fn no_variant_is_ever_reported_as_the_unspecified_code() {
    // ERROR_CODE_UNSPECIFIED means "the agent failed without classifying the
    // failure" (common.proto).
    for error in every_variant() {
        assert_ne!(
            to_agent_error(&error).code,
            ErrorCode::Unspecified as i32,
            "{error:?} must be classified"
        );
    }
}

#[test]
fn no_mapped_failure_carries_a_programs_output() {
    for error in every_variant() {
        let wire = to_agent_error(&error);
        assert!(
            wire.tool_output.is_empty(),
            "{error:?} must not carry tool output"
        );
        assert_eq!(
            wire.message,
            error.to_string(),
            "the mapping must not invent a message beside the variant's own"
        );
    }
}
