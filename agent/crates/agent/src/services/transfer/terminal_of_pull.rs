//! A finished pull wrapped into the terminal message of its stream.

use maran_ops::transfer::model::pull_summary::PullSummary;

use crate::proto::{
    PullAccountFilesDone, SourceFailureReason, TransferFailureReason, pull_account_files_response,
};
use crate::services::transfer::source_failure::SourceFailure;

/// Wraps the result of a pull into the terminal arm of the response and the two
/// reasons that travel beside it.
///
/// Both reasons are unspecified for a pull that completed; they are set only
/// together with the error.
#[must_use]
pub fn terminal_of_pull(
    result: Result<PullSummary, SourceFailure>,
) -> (
    pull_account_files_response::Result,
    SourceFailureReason,
    TransferFailureReason,
) {
    match result {
        Ok(summary) => (
            pull_account_files_response::Result::Done(PullAccountFilesDone {
                slices: summary.slices,
                source_bytes: summary.source_bytes.unwrap_or(0),
                source_bytes_known: summary.source_bytes.is_some(),
            }),
            SourceFailureReason::Unspecified,
            TransferFailureReason::Unspecified,
        ),
        Err(failure) => (
            pull_account_files_response::Result::Error(failure.error),
            failure.reason,
            failure.transfer_reason,
        ),
    }
}

#[cfg(test)]
#[path = "../../tests/services/transfer/terminal_of_pull_tests.rs"]
mod tests;
