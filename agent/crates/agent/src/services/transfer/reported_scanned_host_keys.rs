//! A finished host key scan onto its response.

use maran_ops::transfer::ScannedHostKey;

use crate::proto::{ScanHostKeyResponse, SourceFailureReason, scan_host_key_response};
use crate::services::transfer::source_failure::SourceFailure;
use crate::services::transfer::to_wire_scanned_host_keys::to_wire_scanned_host_keys;

/// Builds the response a scan ends with. The connection reason travels beside the
/// error and only with it.
#[must_use]
pub fn reported_scanned_host_keys(
    outcome: Result<Vec<ScannedHostKey>, SourceFailure>,
) -> ScanHostKeyResponse {
    match outcome {
        Ok(keys) => ScanHostKeyResponse {
            result: Some(scan_host_key_response::Result::Ok(
                to_wire_scanned_host_keys(&keys),
            )),
            failure_reason: SourceFailureReason::Unspecified as i32,
        },
        Err(failure) => ScanHostKeyResponse {
            result: Some(scan_host_key_response::Result::Error(failure.error)),
            failure_reason: failure.reason as i32,
        },
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/reported_scanned_host_keys_tests.rs"]
mod tests;
