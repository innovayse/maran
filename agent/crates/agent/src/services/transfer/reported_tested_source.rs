//! A finished connection test onto its response.

use maran_ops::transfer::TestedSource;

use crate::proto::{SourceFailureReason, TestSourceResponse, test_source_response};
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::to_wire_tested_source::to_wire_tested_source;

/// Builds the response a connection test ends with. The connection reason travels
/// beside the error and only with it.
#[must_use]
pub fn reported_tested_source(outcome: Result<TestedSource, SourceFailure>) -> TestSourceResponse {
    match outcome {
        Ok(tested) => TestSourceResponse {
            result: Some(test_source_response::Result::Ok(to_wire_tested_source(
                &tested,
            ))),
            failure_reason: SourceFailureReason::Unspecified as i32,
        },
        Err(failure) => TestSourceResponse {
            result: Some(test_source_response::Result::Error(failure.error)),
            failure_reason: failure.reason as i32,
        },
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/reported_tested_source_tests.rs"]
mod tests;
